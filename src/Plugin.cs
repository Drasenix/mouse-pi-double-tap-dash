using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace MouseDoubleTapDash
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.cono.mousedoubletapdash";
        public const string Name = "Mouse Double Tap Dash";
        public const string Version = "1.2.2";

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _windowMs;
        private ConfigEntry<int> _cooldownMs;
        private ConfigEntry<string> _dashKeyOverride;
        private ConfigEntry<bool> _verbose;

        private static bool _refreshRequested;
        private static bool _bindingsLoaded;
        private static bool _inputHooksInstalled;

        private InputAction _moveAction;
        private InputAction _dashAction;
        private readonly Key[] _moveKeys = { Key.W, Key.S, Key.A, Key.D };
        private Key _dashKey = Key.LeftAlt;

        private bool _activeResolved;
        private bool _profileLogged;
        private float _nextResolveTime;

        private readonly Dictionary<Key, float> _lastTapTime = new Dictionary<Key, float>();
        private float _lastDashTime = -999f;

        private bool _syntheticPressActive;
        private float _syntheticReleaseTime;
        private bool _dashKeyWasHeldBefore;

        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true,
                "Active le dash par double-appui sur une touche de déplacement.");
            _windowMs = Config.Bind("Timing", "DoubleTapWindowMs", 250,
                "Fenêtre maximale entre les deux appuis, en millisecondes.");
            _cooldownMs = Config.Bind("Timing", "CooldownMs", 350,
                "Délai minimum entre deux dashs déclenchés par le mod, en millisecondes.");
            _dashKeyOverride = Config.Bind("General", "DashKeyOverride", "",
                "Force la touche de dash (ex: LeftShift). Vide = lecture automatique du binding runtime.");
            _verbose = Config.Bind("Debug", "Verbose", false,
                "Écrit un log à chaque dash déclenché.");

            Logger.LogInfo("[MouseDoubleTapDash] loaded v" + Version);

            RemoveObsoleteConfig();
            ApplyFallbackBindings();
            TryInstallInputHooks();
            MenuIntegration.Initialize(Logger, OnMenuEnabledChanged, _enabled.Value, _verbose.Value);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (_syntheticPressActive && now >= _syntheticReleaseTime)
                ReleaseSyntheticPress();

            MenuIntegration.Tick();

            if (_refreshRequested)
            {
                _refreshRequested = false;
                _nextResolveTime = 0f;
            }

            if (!_enabled.Value)
                return;

            if (Keyboard.current == null)
                return;

            if (now >= _nextResolveTime)
            {
                _nextResolveTime = now + (_activeResolved ? 10f : 2f);
                ResolveInput(false);
            }

            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            if (Time.timeScale <= 0f)
                return;

            if (_moveAction != null && !_moveAction.enabled)
                return;

            if (_dashAction != null && !_dashAction.enabled)
                return;

            for (int i = 0; i < _moveKeys.Length; i++)
            {
                Key key = _moveKeys[i];
                if (key == Key.None)
                    continue;

                KeyControl control = Keyboard.current[key];
                if (control == null || !control.wasPressedThisFrame)
                    continue;

                if (_lastTapTime.TryGetValue(key, out float previous) &&
                    now - previous <= _windowMs.Value / 1000f &&
                    now - previous >= 0.03f)
                {
                    _lastTapTime[key] = -999f;
                    TryDash(now);
                }
                else
                {
                    _lastTapTime[key] = now;
                }
            }
        }

        private void OnMenuEnabledChanged(bool value)
        {
            if (!value && _syntheticPressActive)
                ReleaseSyntheticPress();

            _enabled.Value = value;
            Config.Save();
            Logger.LogInfo("[MouseDoubleTapDash] " + (value ? "ENABLED" : "DISABLED") + " (menu)");
        }

        private void OnDestroy()
        {
            if (_syntheticPressActive)
                ReleaseSyntheticPress();
        }

        private void RemoveObsoleteConfig()
        {
            try
            {
                ConfigDefinition obsolete = new ConfigDefinition("General", "ToggleKey");
                Config.Remove(obsolete);

                PropertyInfo orphanedProperty = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (orphanedProperty != null &&
                    orphanedProperty.GetValue(Config) is IDictionary<ConfigDefinition, string> orphaned)
                {
                    orphaned.Remove(obsolete);
                }

                Config.Save();
            }
            catch (Exception e)
            {
                Logger.LogWarning("[MouseDoubleTapDash] config migration failed: " + e.Message);
            }
        }

        private void ApplyFallbackBindings()
        {
            _moveKeys[0] = Key.W;
            _moveKeys[1] = Key.S;
            _moveKeys[2] = Key.A;
            _moveKeys[3] = Key.D;

            Key profileDash = ReadProfileDashKey();
            _dashKey = profileDash != Key.None ? profileDash : Key.LeftAlt;
        }

        private static void TryInstallInputHooks()
        {
            if (_inputHooksInstalled)
                return;

            Type inputManagerType = FindType("InputManager");
            if (inputManagerType != null)
                InstallInputHooks(inputManagerType);
        }

        private void ResolveInput(bool logDetails)
        {
            if (Keyboard.current == null)
            {
                if (logDetails)
                    Logger.LogInfo("[MouseDoubleTapDash] input system not ready, waiting");
                return;
            }

            Key[] oldMoves = { _moveKeys[0], _moveKeys[1], _moveKeys[2], _moveKeys[3] };
            Key oldDash = _dashKey;

            InputActionAsset asset = null;
            bool active = false;

            if (TryGetInputManagerAsset(out asset))
                active = true;
            else if (TryGetHubPlayerInputAsset(out asset))
                active = true;
            else
                asset = TryGetGenericAsset();

            if (asset == null)
            {
                if (!_activeResolved)
                {
                    _moveKeys[0] = Key.W;
                    _moveKeys[1] = Key.S;
                    _moveKeys[2] = Key.A;
                    _moveKeys[3] = Key.D;

                    Key fallbackDash = ReadProfileDashKey();
                    _dashKey = fallbackDash != Key.None ? fallbackDash : Key.LeftAlt;
                }

                if (logDetails)
                    Logger.LogInfo("[MouseDoubleTapDash] input asset not ready, fallback W/A/S/D + " + _dashKey);
                return;
            }

            if (!active && _activeResolved)
                return;

            InputAction moveAction = asset.FindAction("Player/Move");
            InputAction dashAction = asset.FindAction("Player/Dash");
            if (moveAction == null || dashAction == null)
                return;

            Key[] parsedMoves = ParseMoveBindings(moveAction);

            Key overrideDash = Key.None;
            if (!string.IsNullOrWhiteSpace(_dashKeyOverride.Value))
                overrideDash = ParseKey(_dashKeyOverride.Value.Trim(), Key.None);

            Key profileDash = ReadProfileDashKey();
            if (!_profileLogged)
            {
                _profileLogged = true;
                Logger.LogInfo("[MouseDoubleTapDash] profile dash=" + profileDash +
                               " path=" + Application.persistentDataPath);
            }

            Key assetDash = active ? GetDashKey(dashAction) : Key.None;
            Key parsedDash;
            string dashSource;

            if (overrideDash != Key.None)
            {
                parsedDash = overrideDash;
                dashSource = "override";
            }
            else if (!_bindingsLoaded && profileDash != Key.None)
            {
                parsedDash = profileDash;
                dashSource = "profile-pending";
            }
            else if (active && assetDash != Key.None)
            {
                parsedDash = assetDash;
                dashSource = "runtime";
            }
            else if (profileDash != Key.None)
            {
                parsedDash = profileDash;
                dashSource = "profile";
            }
            else
            {
                parsedDash = Key.LeftAlt;
                dashSource = "fallback";
            }

            _moveKeys[0] = parsedMoves[0] != Key.None ? parsedMoves[0] : Key.W;
            _moveKeys[1] = parsedMoves[1] != Key.None ? parsedMoves[1] : Key.S;
            _moveKeys[2] = parsedMoves[2] != Key.None ? parsedMoves[2] : Key.A;
            _moveKeys[3] = parsedMoves[3] != Key.None ? parsedMoves[3] : Key.D;
            _dashKey = parsedDash;

            _moveAction = moveAction;
            _dashAction = dashAction;
            if (active)
                _activeResolved = true;

            bool changed = oldDash != _dashKey ||
                           oldMoves[0] != _moveKeys[0] ||
                           oldMoves[1] != _moveKeys[1] ||
                           oldMoves[2] != _moveKeys[2] ||
                           oldMoves[3] != _moveKeys[3];

            if (logDetails || changed)
            {
                Logger.LogInfo("[MouseDoubleTapDash] bindings: up=" + _moveKeys[0] +
                               " down=" + _moveKeys[1] +
                               " left=" + _moveKeys[2] +
                               " right=" + _moveKeys[3] +
                               " dash=" + _dashKey +
                               " (source=" + dashSource +
                               ", activeAsset=" + active +
                               ", assetDash=" + assetDash +
                               ", profileDash=" + profileDash +
                               ", bindingsLoaded=" + _bindingsLoaded + ")");
            }
        }

        private static Key[] ParseMoveBindings(InputAction moveAction)
        {
            Key[] keys = { Key.None, Key.None, Key.None, Key.None };

            foreach (InputBinding binding in moveAction.bindings)
            {
                if (!binding.isPartOfComposite)
                    continue;

                string part = (binding.name ?? string.Empty).ToLowerInvariant();
                int index;
                switch (part)
                {
                    case "up": index = 0; break;
                    case "down": index = 1; break;
                    case "left": index = 2; break;
                    case "right": index = 3; break;
                    default: continue;
                }

                string path = string.IsNullOrEmpty(binding.effectivePath) ? binding.path : binding.effectivePath;
                Key key = ParseKey(path, Key.None);
                if (key != Key.None)
                    keys[index] = key;
            }

            return keys;
        }

        private static Key GetDashKey(InputAction dashAction)
        {
            foreach (InputBinding binding in dashAction.bindings)
            {
                if (binding.isPartOfComposite)
                    continue;

                string path = string.IsNullOrEmpty(binding.effectivePath) ? binding.path : binding.effectivePath;
                if (path == null || !path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
                    continue;

                Key key = ParseKey(path, Key.None);
                if (key != Key.None)
                    return key;
            }

            return Key.None;
        }

        private static bool TryGetInputManagerAsset(out InputActionAsset asset)
        {
            asset = null;

            Type inputManagerType = FindType("InputManager");
            if (inputManagerType == null)
                return false;

            InstallInputHooks(inputManagerType);

            try
            {
                UnityEngine.Object[] managers = Resources.FindObjectsOfTypeAll(inputManagerType);
                if (managers != null)
                {
                    for (int i = 0; i < managers.Length; i++)
                    {
                        if (managers[i] != null &&
                            TryGetControlsAsset(managers[i], inputManagerType, out asset))
                            return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static void InstallInputHooks(Type inputManagerType)
        {
            if (_inputHooksInstalled)
                return;

            try
            {
                FieldInfo loadField = inputManagerType.GetField("OnLoadBindings",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo saveField = inputManagerType.GetField("OnSaveBindings",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                Action handler = OnInputBindingsChanged;
                bool installed = false;

                if (loadField != null)
                {
                    loadField.SetValue(null, (Action)Delegate.Combine((Action)loadField.GetValue(null), handler));
                    installed = true;
                }

                if (saveField != null)
                {
                    saveField.SetValue(null, (Action)Delegate.Combine((Action)saveField.GetValue(null), handler));
                    installed = true;
                }

                if (installed)
                    _inputHooksInstalled = true;
            }
            catch
            {
                // ignore
            }
        }

        private static void OnInputBindingsChanged()
        {
            _bindingsLoaded = true;
            _refreshRequested = true;
        }

        private static bool TryGetControlsAsset(object instance, Type inputManagerType, out InputActionAsset asset)
        {
            asset = null;

            try
            {
                PropertyInfo controlsProperty = inputManagerType.GetProperty("Controls",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object controls = controlsProperty?.GetValue(instance);
                if (controls == null)
                    return false;

                PropertyInfo assetProperty = controls.GetType().GetProperty("asset",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                asset = assetProperty?.GetValue(controls) as InputActionAsset;
                return asset != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetHubPlayerInputAsset(out InputActionAsset asset)
        {
            asset = null;

            Type hubType = FindType("HubPlayerInput");
            if (hubType == null)
                return false;

            try
            {
                UnityEngine.Object[] hubs = Resources.FindObjectsOfTypeAll(hubType);
                FieldInfo controlsField = hubType.GetField("controls",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                for (int i = 0; i < hubs.Length; i++)
                {
                    object hub = hubs[i];
                    if (hub == null || controlsField == null)
                        continue;

                    object controls = controlsField.GetValue(hub);
                    if (controls == null)
                        continue;

                    PropertyInfo assetProperty = controls.GetType().GetProperty("asset",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    asset = assetProperty?.GetValue(controls) as InputActionAsset;
                    if (asset != null)
                        return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static InputActionAsset TryGetGenericAsset()
        {
            try
            {
                InputActionAsset[] assets = Resources.FindObjectsOfTypeAll<InputActionAsset>();
                for (int i = 0; i < assets.Length; i++)
                {
                    InputActionAsset candidate = assets[i];
                    if (candidate != null &&
                        candidate.FindAction("Player/Move") != null &&
                        candidate.FindAction("Player/Dash") != null)
                        return candidate;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static Key ReadProfileDashKey()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "Save", "profile.rsf");
                if (!System.IO.File.Exists(path))
                    return Key.None;

                string text = System.IO.File.ReadAllText(path);
                int actionIndex = text.IndexOf("Player/Dash", StringComparison.Ordinal);
                if (actionIndex < 0)
                    return Key.None;

                int pathIndex = text.IndexOf("<Keyboard>/", actionIndex, StringComparison.Ordinal);
                if (pathIndex < 0)
                    return Key.None;

                int start = pathIndex + "<Keyboard>/".Length;
                int end = start;
                while (end < text.Length &&
                       text[end] != '"' &&
                       text[end] != '\\' &&
                       text[end] != '\n' &&
                       text[end] != '\r')
                {
                    end++;
                }

                string keyName = text.Substring(start, end - start);
                return ParseKey(keyName, Key.None);
            }
            catch
            {
                return Key.None;
            }
        }

        private void TryDash(float now)
        {
            if (Keyboard.current == null)
                return;

            if (now - _lastDashTime < _cooldownMs.Value / 1000f)
                return;

            if (_syntheticPressActive)
                return;

            if (_dashKey == Key.None)
                return;

            var held = new List<Key>();
            foreach (KeyControl control in Keyboard.current.allKeys)
            {
                if (control != null && control.isPressed)
                    held.Add(control.keyCode);
            }

            _dashKeyWasHeldBefore = false;
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i] == _dashKey)
                {
                    _dashKeyWasHeldBefore = true;
                    break;
                }
            }

            var pressed = new List<Key>(held);
            if (!_dashKeyWasHeldBefore)
                pressed.Add(_dashKey);

            try
            {
                InputSystem.QueueStateEvent(
                    Keyboard.current,
                    new KeyboardState(pressed.ToArray()),
                    InputState.currentTime);
            }
            catch (Exception e)
            {
                if (_verbose.Value)
                    Logger.LogWarning("[MouseDoubleTapDash] failed to queue dash key: " + e.Message);
                return;
            }

            _syntheticPressActive = true;
            _syntheticReleaseTime = now + 0.06f;
            _lastDashTime = now;

            if (_verbose.Value)
                Logger.LogInfo("[MouseDoubleTapDash] dash triggered (" + _dashKey + ")");
        }

        private void ReleaseSyntheticPress()
        {
            if (Keyboard.current == null)
            {
                _syntheticPressActive = false;
                return;
            }

            try
            {
                var held = new List<Key>();
                foreach (KeyControl control in Keyboard.current.allKeys)
                {
                    if (control != null && control.isPressed)
                        held.Add(control.keyCode);
                }

                if (!_dashKeyWasHeldBefore)
                    held.RemoveAll(IsDashKey);

                InputSystem.QueueStateEvent(
                    Keyboard.current,
                    new KeyboardState(held.ToArray()),
                    InputState.currentTime);
            }
            catch (Exception e)
            {
                if (_verbose.Value)
                    Logger.LogWarning("[MouseDoubleTapDash] failed to release dash key: " + e.Message);
            }
            finally
            {
                _syntheticPressActive = false;
            }
        }

        private bool IsDashKey(Key key)
        {
            return key == _dashKey;
        }

        private static Type FindType(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(typeName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static Key ParseKey(string path, Key fallback)
        {
            if (string.IsNullOrEmpty(path))
                return fallback;

            string value = path.Trim();
            int slash = value.LastIndexOf('/');
            if (slash >= 0)
                value = value.Substring(slash + 1);

            value = value.Trim('<', '>');

            if (Enum.TryParse(value, true, out Key key) && key != Key.None)
                return key;

            return fallback;
        }
    }
}

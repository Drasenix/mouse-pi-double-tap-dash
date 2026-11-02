using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MouseDoubleTapDash
{
    internal static class MenuIntegration
    {
        private static ManualLogSource _logger;
        private static Action<bool> _onChanged;
        private static bool _initialEnabled;
        private static bool _verbose;

        private static Type _settingType;
        private static object _setting;
        private static MethodInfo _getAsBool;
        private static MethodInfo _setValueWithoutNotify;
        private static MethodInfo _alertOnValueChanged;

        private static Type _pageType;
        private static Type _rowType;
        private static Type _toggleType;
        private static Type _stepperType;
        private static Type _textType;
        private static object _rowNameText;

        private static Harmony _harmony;
        private static bool _patched;
        private static bool _injected;
        private static float _nextTryTime;
        private static float _nextSettingTryTime;
        private static bool _syncing;
        private static bool _boolLabelsCopied;
        private static bool _errorLogged;

        public static void Initialize(ManualLogSource logger, Action<bool> onChanged, bool initial, bool verbose)
        {
            _logger = logger;
            _onChanged = onChanged;
            _initialEnabled = initial;
            _verbose = verbose;

            CreateSetting();
            ApplyPatches();
            TryInject();
        }

        public static void Tick()
        {
            if (_setting == null && Time.unscaledTime >= _nextSettingTryTime)
            {
                _nextSettingTryTime = Time.unscaledTime + 2f;
                CreateSetting();
            }

            if (!_patched)
                ApplyPatches();

            if (!_injected && Time.unscaledTime >= _nextTryTime)
            {
                _nextTryTime = Time.unscaledTime + 2f;
                TryInject();
            }
        }

        public static void SyncFromConfig(bool value)
        {
            _initialEnabled = value;

            if (_setting == null)
                return;

            try
            {
                _syncing = true;
                SetField(_setting, "defaultValue", value);
                _setValueWithoutNotify?.Invoke(_setting, new object[] { value });
                _alertOnValueChanged?.Invoke(_setting, null);
            }
            catch (Exception e)
            {
                LogError("SyncFromConfig: " + e.Message);
            }
            finally
            {
                _syncing = false;
            }
        }

        private static void CreateSetting()
        {
            if (_setting != null)
                return;

            if (_settingType == null)
                _settingType = FindType("MouseSettings.SettingBool");

            if (_settingType == null)
                return;

            try
            {
                _setting = ScriptableObject.CreateInstance(_settingType);
                ((UnityEngine.Object)_setting).name = "DoubleTapDashEnabled";

                InitializeLists(_setting);
                SetField(_setting, "defaultValue", _initialEnabled);
                SetField(_setting, "isCloudSave", false);
                SetEnumField(_setting, "displayType", "MouseSettings.SettingDisplayType", "Stepper");

                _getAsBool = _settingType.GetMethod("GetAsBool", BindingFlags.Instance | BindingFlags.Public);
                _setValueWithoutNotify = _settingType.GetMethod("SetValueWithoutNotify", BindingFlags.Instance | BindingFlags.Public);
                _alertOnValueChanged = _settingType.GetMethod("AlertOnValueChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                _setValueWithoutNotify?.Invoke(_setting, new object[] { _initialEnabled });

                EventInfo changedEvent = _settingType.GetEvent("OnValueChanged", BindingFlags.Instance | BindingFlags.Public);
                if (changedEvent != null)
                    changedEvent.AddEventHandler(_setting, new Action(OnMenuValueChanged));

                _logger.LogInfo("[MouseDoubleTapDash] menu setting created");
            }
            catch (Exception e)
            {
                LogError("CreateSetting: " + e);
                _setting = null;
            }
        }

        private static void OnMenuValueChanged()
        {
            if (_syncing || _setting == null || _getAsBool == null)
                return;

            try
            {
                bool value = (bool)_getAsBool.Invoke(_setting, null);
                if (value == _initialEnabled)
                    return;

                _initialEnabled = value;
                SetField(_setting, "defaultValue", value);
                _onChanged?.Invoke(value);
            }
            catch (Exception e)
            {
                LogError("OnMenuValueChanged: " + e.Message);
            }
        }

        private static void ApplyPatches()
        {
            if (_patched)
                return;

            if (_pageType == null) _pageType = FindType("UI.Notebook.Settings.SettingsPageUI");
            if (_rowType == null) _rowType = FindType("UI.Notebook.Settings.SettingsListSelectableUI");
            if (_toggleType == null) _toggleType = FindType("UI.ValueChangeSelectableToggle");
            if (_stepperType == null) _stepperType = FindType("UI.ValueChangeSelectableStepper");
            if (_textType == null) _textType = FindType("ProjectLocalizedText");

            if (_pageType == null || _rowType == null || _toggleType == null)
                return;

            try
            {
                _harmony = new Harmony("com.cono.mousedoubletapdash.menu");

                MethodInfo setup = FindMethod(_rowType, "Setup", 3);
                MethodInfo updateName = FindMethod(_rowType, "UpdateName", 0);
                MethodInfo refresh = FindMethod(_toggleType, "RefreshValueDisplay", 0);

                if (setup != null)
                    _harmony.Patch(setup, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(RowSetupPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

                if (updateName != null)
                    _harmony.Patch(updateName, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(RowUpdatePostfix), BindingFlags.Static | BindingFlags.NonPublic)));

                if (refresh != null)
                    _harmony.Patch(refresh, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(ToggleRefreshPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

                MethodInfo stepperRefresh = _stepperType != null
                    ? FindMethod(_stepperType, "RefreshValueDisplay", 0)
                    : null;
                if (stepperRefresh != null)
                    _harmony.Patch(stepperRefresh, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(StepperRefreshPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

                MethodInfo updateText = _textType != null
                    ? FindMethod(_textType, "UpdateText", 1)
                    : null;
                if (updateText != null)
                    _harmony.Patch(updateText, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(LocalizedTextPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

                _patched = true;
                _logger.LogInfo("[MouseDoubleTapDash] menu Harmony patches applied");
            }
            catch (Exception e)
            {
                LogError("ApplyPatches: " + e.Message);
            }
        }

        private static void TryInject()
        {
            if (_injected || _setting == null)
                return;

            if (_pageType == null)
                _pageType = FindType("UI.Notebook.Settings.SettingsPageUI");

            if (_pageType == null)
                return;

            TryCopyBoolLabels();

            try
            {
                UnityEngine.Object[] pages = Resources.FindObjectsOfTypeAll(_pageType);
                if (pages == null || pages.Length == 0)
                    return;

                object fallbackPage = null;
                object fallbackGroup = null;

                for (int i = 0; i < pages.Length; i++)
                {
                    object page = pages[i];
                    if (page == null)
                        continue;

                    IList groups = GetField(page, "settingsGroups") as IList;
                    if (groups == null || groups.Count == 0)
                        continue;

                    bool isVideo = GetField(page, "settingResolution") != null;
                    object firstGroup = null;

                    for (int g = 0; g < groups.Count; g++)
                    {
                        object group = groups[g];
                        if (group == null)
                            continue;

                        IList assets = GetField(group, "settingAssets") as IList;
                        if (assets == null || assets.Count == 0)
                            continue;

                        object rect = GetField(group, "groupListRectTransform");
                        if (rect == null)
                            continue;

                        if (firstGroup == null)
                            firstGroup = group;

                        if (!isVideo)
                        {
                            if (!assets.Contains(_setting))
                                assets.Add(_setting);
                            InjectIntoPage(page);
                            return;
                        }
                    }

                    if (!isVideo && firstGroup != null)
                    {
                        IList assets = GetField(firstGroup, "settingAssets") as IList;
                        if (assets != null && !assets.Contains(_setting))
                            assets.Add(_setting);
                        InjectIntoPage(page);
                        return;
                    }

                    if (fallbackPage == null && firstGroup != null)
                    {
                        fallbackPage = page;
                        fallbackGroup = firstGroup;
                    }
                }

                if (fallbackPage != null && fallbackGroup != null)
                {
                    IList assets = GetField(fallbackGroup, "settingAssets") as IList;
                    if (assets != null && !assets.Contains(_setting))
                        assets.Add(_setting);
                    InjectIntoPage(fallbackPage);
                }
            }
            catch (Exception e)
            {
                LogError("TryInject: " + e.Message);
            }
        }

        private static void TryCopyBoolLabels()
        {
            if (_boolLabelsCopied || _setting == null || _settingType == null)
                return;

            try
            {
                UnityEngine.Object[] assets = Resources.FindObjectsOfTypeAll(_settingType);
                if (assets == null)
                    return;

                object fallback = null;

                for (int i = 0; i < assets.Length; i++)
                {
                    object candidate = assets[i];
                    if (candidate == null || ReferenceEquals(candidate, _setting))
                        continue;

                    if (GetField(candidate, "trueName") == null ||
                        GetField(candidate, "falseName") == null)
                        continue;

                    if (fallback == null)
                        fallback = candidate;

                    string candidateName = ((UnityEngine.Object)candidate).name;
                    if (candidateName.IndexOf("invertMouse", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        CopyBoolLabelsFrom(candidate);
                        return;
                    }
                }

                if (fallback != null)
                    CopyBoolLabelsFrom(fallback);
            }
            catch (Exception e)
            {
                LogError("TryCopyBoolLabels: " + e.Message);
            }
        }

        private static void CopyBoolLabelsFrom(object candidate)
        {
            object trueName = GetField(candidate, "trueName");
            object falseName = GetField(candidate, "falseName");
            if (trueName == null || falseName == null)
                return;

            SetField(_setting, "trueName", trueName);
            SetField(_setting, "falseName", falseName);
            _boolLabelsCopied = true;

            _logger.LogInfo("[MouseDoubleTapDash] bool labels copied from '" +
                            ((UnityEngine.Object)candidate).name + "'");

            _syncing = true;
            _alertOnValueChanged?.Invoke(_setting, null);
            _syncing = false;
        }

        private static void InjectIntoPage(object page)
        {
            _injected = true;

            try
            {
                object isSetupValue = GetField(page, "isSetup");
                bool isSetup = isSetupValue is bool value && value;
                if (isSetup)
                {
                    MethodInfo redraw = _pageType.GetMethod("RedrawMenu", BindingFlags.Instance | BindingFlags.Public);
                    redraw?.Invoke(page, null);
                }
            }
            catch (Exception e)
            {
                LogError("RedrawMenu: " + e.Message);
            }

            _logger.LogInfo("[MouseDoubleTapDash] menu toggle injected into settings page '" +
                            ((UnityEngine.Object)page).name + "'");
        }

        private static void RowSetupPostfix(object __instance, object settingAsset)
        {
            if (!ReferenceEquals(settingAsset, _setting))
                return;

            OverrideRowLabel(__instance);
        }

        private static void RowUpdatePostfix(object __instance)
        {
            try
            {
                PropertyInfo property = __instance.GetType().GetProperty("SettingAsset",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object current = property?.GetValue(__instance);
                if (!ReferenceEquals(current, _setting))
                    return;
            }
            catch
            {
                return;
            }

            OverrideRowLabel(__instance);
        }

        private static void ToggleRefreshPostfix(object __instance)
        {
            try
            {
                object current = GetField(__instance, "_settingBool");
                if (!ReferenceEquals(current, _setting))
                    return;

                if (_boolLabelsCopied)
                    return;

                object label = GetField(__instance, "label");
                bool value = (bool)_getAsBool.Invoke(_setting, null);
                OverrideText(label, value ? "Oui" : "Non");
            }
            catch (Exception e)
            {
                LogError("ToggleRefreshPostfix: " + e.Message);
            }
        }

        private static void OverrideRowLabel(object row)
        {
            try
            {
                object nameText = GetField(row, "nameText");
                if (nameText != null)
                    _rowNameText = nameText;

                OverrideText(nameText, "Double-tap dash");
            }
            catch (Exception e)
            {
                LogError("OverrideRowLabel: " + e.Message);
            }
        }

        private static void LocalizedTextPostfix(object __instance)
        {
            if (_rowNameText == null || !ReferenceEquals(__instance, _rowNameText))
                return;

            try
            {
                OverrideText(__instance, "Double-tap dash");
            }
            catch (Exception e)
            {
                LogError("LocalizedTextPostfix: " + e.Message);
            }
        }

        private static void StepperRefreshPostfix(object __instance)
        {
            try
            {
                object current = GetField(__instance, "_settingBool");
                if (!ReferenceEquals(current, _setting))
                    return;

                if (_boolLabelsCopied)
                    return;

                object label = GetField(__instance, "label");
                bool value = (bool)_getAsBool.Invoke(_setting, null);
                OverrideText(label, value ? "Oui" : "Non");
            }
            catch (Exception e)
            {
                LogError("StepperRefreshPostfix: " + e.Message);
            }
        }

        private static void OverrideText(object projectLocalizedText, string text)
        {
            if (projectLocalizedText == null)
                return;

            MethodInfo method = projectLocalizedText.GetType().GetMethod("OverrideText",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);

            method?.Invoke(projectLocalizedText, new object[] { text });
        }

        private static void InitializeLists(object setting)
        {
            Type type = setting.GetType();
            while (type != null)
            {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];

                    if (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        if (field.GetValue(setting) == null)
                            field.SetValue(setting, Activator.CreateInstance(field.FieldType));
                    }
                    else if (field.Name == "_currentValidQualities" &&
                             field.GetValue(setting) == null)
                    {
                        field.SetValue(setting, new bool[16]);
                    }
                }

                type = type.BaseType;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                type = type.BaseType;
            }
            return null;
        }

        private static object GetField(object instance, string name)
        {
            if (instance == null)
                return null;

            FieldInfo field = FindField(instance.GetType(), name);
            return field?.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = FindField(instance.GetType(), name);
            if (field == null)
            {
                if (_verbose && _logger != null)
                    _logger.LogWarning("[MouseDoubleTapDash] field not found: " + name);
                return;
            }

            field.SetValue(instance, value);
        }

        private static void SetEnumField(object instance, string fieldName, string enumTypeName, string valueName)
        {
            Type enumType = FindType(enumTypeName);
            if (enumType == null)
                return;

            object value = Enum.Parse(enumType, valueName);
            SetField(instance, fieldName, value);
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name && methods[i].GetParameters().Length == parameterCount)
                    return methods[i];
            }
            return null;
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

        private static void LogError(string message)
        {
            if (_logger != null && !_errorLogged)
            {
                _logger.LogWarning("[MouseDoubleTapDash] " + message);
                _errorLogged = true;
            }
        }
    }
}

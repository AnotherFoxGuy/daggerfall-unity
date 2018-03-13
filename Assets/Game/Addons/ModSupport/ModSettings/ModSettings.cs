﻿// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2018 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: TheLacus
// Contributors:
//
// Notes:
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using IniParser.Model;

namespace DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings
{
    /// <summary>
    /// Read mod settings.
    /// </summary>
    public class ModSettings
    {
        // Fields
        Mod mod;
        IniData userSettings;
        ModSettingsConfiguration config;

        #region Public Methods

        /// <summary>
        /// Import settings for Mod.
        /// </summary>
        /// <param name="mod">Mod to load settings for.</param>
        public ModSettings(Mod mod)
        {
            if (!mod.HasSettings)
                throw new ArgumentException(string.Format("{0} has no settings.", mod.Title), "mod");

            this.mod = mod;
            ModSettingsReader.GetSettings(mod, out userSettings, out config);
        }

        /// <summary>
        /// Get string from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public string GetString(string section, string name)
        {
            string text = GetValue(section, name);
            if (text != null)
                return text;

            ModSettingsKey key;
            if (config.Key(section, name, ModSettingsKey.KeyType.Text, out key))
                return key.text.text;

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Get integer from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public int GetInt(string section, string name)
        {
            int value;
            if (int.TryParse(GetValue(section, name), out value))
                return value;

            ModSettingsKey key;
            if (config.Key(section, name, out key))
            {
                if (key.type == ModSettingsKey.KeyType.Slider)
                    return key.slider.value;
                if (key.type == ModSettingsKey.KeyType.MultipleChoice)
                    return key.multipleChoice.selected;
            }

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Get integer from user settings or, as fallback, from default settings.
        /// Value is at least equal to min.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        /// <param name="min">Minimum accepted value.</param>
        public int GetInt(string section, string name, int min)
        {
            return Mathf.Max(min, GetInt(section, name));
        }

        /// <summary>
        /// Get integer from user settings or, as fallback, from default settings.
        /// Value is clamped in range (min-max).
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        /// <param name="min">Minimum accepted value.</param>
        /// <param name="max">Maximum accepted value.</param>
        public int GetInt(string section, string name, int min, int max)
        {
            return Mathf.Clamp(GetInt(section, name), min, max);
        }

        /// <summary>
        /// Get float from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public float GetFloat(string section, string name)
        {
            float value;
            if (float.TryParse(GetValue(section, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;

            ModSettingsKey key;
            if (config.Key(section, name, ModSettingsKey.KeyType.FloatSlider, out key))
                return key.floatSlider.value;

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Get float from user settings or, as fallback, from default settings.
        /// Value is at least equal to min.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        /// <param name="min">Minimum accepted value.</param>
        public float GetFloat(string section, string name, float min)
        {
            return Mathf.Max(min, GetFloat(section, name));
        }

        /// <summary>
        /// Get float from user settings or, as fallback, from default settings.
        /// Value is clamped in range (min-max).
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        /// <param name="min">Minimum accepted value.</param>
        /// <param name="max">Maximum accepted value.</param>
        public float GetFloat(string section, string name, float min, float max)
        {
            return Mathf.Clamp(GetFloat(section, name), min, max);
        }

        /// <summary>
        /// Get bool from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public bool GetBool(string section, string name)
        {
            bool value;
            if (bool.TryParse(GetValue(section, name), out value))
                return value;

            ModSettingsKey key;
            if (config.Key(section, name, ModSettingsKey.KeyType.Toggle, out key))
                return key.toggle.value;

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Get color from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public Color GetColor(string section, string name)
        {
            Color color;
            if (ColorUtility.TryParseHtmlString("#" + GetValue(section, name), out color))
                return color;

            ModSettingsKey key;
            if (config.Key(section, name, ModSettingsKey.KeyType.Color, out key))
                return key.color.color;

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Get Tuple from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public DaggerfallWorkshop.Utility.Tuple<int, int> GetTupleInt(string section, string name)
        {
            int first, second;
            var tuple = GetTuple(section, name);
            if (int.TryParse(tuple.First, out first) && int.TryParse(tuple.Second, out second))
                return new DaggerfallWorkshop.Utility.Tuple<int, int>(first, second);

            ModSettingsKey key;
            if (config.Key(section, name, ModSettingsKey.KeyType.Tuple, out key))
                return new DaggerfallWorkshop.Utility.Tuple<int, int>(key.tuple.first, key.tuple.second);

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Get Tuple from user settings or, as fallback, from default settings.
        /// </summary>
        /// <param name="section">Name of section.</param>
        /// <param name="name">Name of key.</param>
        public DaggerfallWorkshop.Utility.Tuple<float, float> GetTupleFloat(string section, string name)
        {
            float first, second;
            var tuple = GetTuple(section, name);
            if (float.TryParse(tuple.First, out first) && float.TryParse(tuple.Second, out second))
                return new DaggerfallWorkshop.Utility.Tuple<float, float>(first, second);

            ModSettingsKey key;
            if (config.Key(section, name, ModSettingsKey.KeyType.FloatTuple, out key))
                return new DaggerfallWorkshop.Utility.Tuple<float, float>(key.tuple.first, key.tuple.second);

            throw NewMissingKeyException(section, name);
        }

        /// <summary>
        /// Deserialize a section of settings in a class.
        /// </summary>
        /// <typeparam name="T">Type of class.</typeparam>
        /// <param name="section">Name of section.</param>
        /// <param name="instance">Instance of class with keys as public fields.</param>
        public void Deserialize<T>(string section, ref T instance) where T : class
        {
            if (!userSettings.Sections.ContainsSection(section))
            {
                Debug.LogErrorFormat("Failed to parse section {0} for mod {1}", section, mod.Title);
                return;
            }

            KeyDataCollection sectionData = userSettings[section];
            foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                try
                {
                    if (sectionData.ContainsKey(field.Name))
                    {
                        var value = GetValue(section, field.Name, field.FieldType);
                        if (value != null)
                            field.SetValue(instance, value);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogErrorFormat("Failed to parse section {0} for mod {1}\n{2}", section, mod.Title, e.ToString());
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Get value from user settings.
        /// </summary>
        private string GetValue(string section, string name)
        {
            KeyDataCollection keyDataCollection = userSettings[section];
            if (keyDataCollection != null)
            {
                string key = keyDataCollection[name];
                if (key != null)
                    return key;
            }

            Debug.LogErrorFormat("Failed to get ({0},{1}) for mod {2}.", section, name, mod.Title);
            return null;
        }

        /// <summary>
        /// Get value for specified type.
        /// </summary>
        private object GetValue(string section, string name, Type type)
        {
            if (type == typeof(string))
                return GetString(section, name);
            else if (type == typeof(int))
                return GetInt(section, name);
            else if (type == typeof(float))
                return GetFloat(section, name);
            else if (type == typeof(bool))
                return GetBool(section, name);
            else if (type == typeof(DaggerfallWorkshop.Utility.Tuple<int, int>))
                return GetTupleInt(section, name);
            else if (type == typeof(DaggerfallWorkshop.Utility.Tuple<float, float>))
                return GetTupleFloat(section, name);
            if (type == typeof(Color))
                return GetColor(section, name);

            return null;
        }

        private DaggerfallWorkshop.Utility.Tuple<string, string> GetTuple(string section, string name)
        {
            try
            {
                string text = GetValue(section, name);
                int index = text.IndexOf(ModSettingsReader.tupleDelimiterChar);
                return new DaggerfallWorkshop.Utility.Tuple<string, string>(text.Substring(0, index), text.Substring(index + ModSettingsReader.tupleDelimiterChar.Length));
            }
            catch { return new DaggerfallWorkshop.Utility.Tuple<string, string>(string.Empty, string.Empty); }
        }

        /// <summary>
        /// Helper that create and return a new KeyNotFoundException exception with a message.
        /// </summary>
        private Exception NewMissingKeyException(string section, string name)
        {
            return new KeyNotFoundException(string.Format("The key ({0},{1}) was not present in {2} settings.", section, name, mod.Title));
        }

        #endregion
    }
}
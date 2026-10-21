using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HKMP.Rounds.Settings
{
    internal sealed class GlobalSettingsStore
    {
        private sealed class SettingsData
        {
            public bool AutoStart;
            public int RoundSoul = 198;
            public bool DisableEnemies;
            public bool DebugDisableShade;
            public bool TimerIntegrationEnabled;
            public int ReadyRequiredCount;
        }

        private const int DefaultRoundSoul = 198;
        private const int MaximumRoundSoul = 198;
        private const int DefaultReadyRequiredCount = 0;
        private const int MaximumReadyRequiredCount = 1000;

        private readonly string _filePath;

        public event Action AutoStartDisabled;

        public bool AutoStart
        {
            get;
            private set;
        }

        public int RoundSoul
        {
            get;
            private set;
        }

        public bool DisableEnemies
        {
            get;
            private set;
        }

        public bool DebugDisableShade
        {
            get;
            private set;
        }

        public bool TimerIntegrationEnabled
        {
            get;
            private set;
        }

        public int ReadyRequiredCount
        {
            get;
            private set;
        }

        public GlobalSettingsStore()
        {
            _filePath =
                Path.Combine(
                    Application.persistentDataPath,
                    "HKMP.Rounds.GlobalSettings.json");

            Load();
        }

        public void SetAutoStart(
            bool enabled)
        {
            AutoStart = enabled;
            Save();

            if (!enabled)
            {
                Action handler = AutoStartDisabled;

                if (handler != null)
                {
                    handler();
                }
            }
        }

        public bool SetRoundSoul(
            int amount)
        {
            if (amount < 0 ||
                amount > MaximumRoundSoul)
            {
                return false;
            }

            RoundSoul = amount;
            Save();
            return true;
        }

        public bool SetReadyRequiredCount(
            int count)
        {
            if (count < 0 ||
                count > MaximumReadyRequiredCount)
            {
                return false;
            }

            ReadyRequiredCount = count;
            Save();
            return true;
        }

        public void SetDisableEnemies(
            bool enabled)
        {
            DisableEnemies = enabled;
            Save();
        }

        public void SetDebugDisableShade(
            bool enabled)
        {
            DebugDisableShade = enabled;
            Save();
        }

        public void SetTimerIntegration(
            bool enabled)
        {
            TimerIntegrationEnabled = enabled;
            Save();
        }

        private void Load()
        {
            AutoStart = false;
            RoundSoul = DefaultRoundSoul;
            DisableEnemies = false;
            DebugDisableShade = false;
            TimerIntegrationEnabled = false;
            ReadyRequiredCount = DefaultReadyRequiredCount;

            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }

                string json =
                    File.ReadAllText(_filePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                SettingsData data =
                    JsonConvert.DeserializeObject<SettingsData>(json);

                if (data == null)
                {
                    return;
                }

                AutoStart = data.AutoStart;
                RoundSoul = data.RoundSoul;
                DisableEnemies = data.DisableEnemies;
                DebugDisableShade = data.DebugDisableShade;
                TimerIntegrationEnabled = data.TimerIntegrationEnabled;
                ReadyRequiredCount = data.ReadyRequiredCount;

                if (RoundSoul < 0 ||
                    RoundSoul > MaximumRoundSoul)
                {
                    RoundSoul = DefaultRoundSoul;
                }

                if (ReadyRequiredCount < 0 ||
                    ReadyRequiredCount > MaximumReadyRequiredCount)
                {
                    ReadyRequiredCount = DefaultReadyRequiredCount;
                }
            }
            catch (Exception exception)
            {
                AutoStart = false;
                RoundSoul = DefaultRoundSoul;
                DisableEnemies = false;
                DebugDisableShade = false;
                TimerIntegrationEnabled = false;
                ReadyRequiredCount = DefaultReadyRequiredCount;

                Modding.Logger.LogError(
                    "[HKMP.Rounds] Failed to load global settings. Exception=" +
                    exception);
            }
        }

        private void Save()
        {
            try
            {
                SettingsData data =
                    new SettingsData
                    {
                        AutoStart = AutoStart,
                        RoundSoul = RoundSoul,
                        DisableEnemies = DisableEnemies,
                        DebugDisableShade = DebugDisableShade,
                        TimerIntegrationEnabled = TimerIntegrationEnabled,
                        ReadyRequiredCount = ReadyRequiredCount
                    };

                string json =
                    JsonConvert.SerializeObject(
                        data,
                        Formatting.Indented);

                File.WriteAllText(
                    _filePath,
                    json);
            }
            catch (Exception exception)
            {
                Modding.Logger.LogError(
                    "[HKMP.Rounds] Failed to save global settings. Exception=" +
                    exception);
            }
        }
    }
}
using System;
using System.IO;
using UnityEngine;

namespace LlamAcademy.Dinos.Companion
{
    /// Versioned, atomic relationship persistence. The evaluator always consumes a sanitized runtime copy.
    public static class CompanionSaveService
    {
        public const int CurrentSchemaVersion = CompanionProfileData.CurrentSchemaVersion;
        public const string FileName = "companion-profile-v2.json";
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        [Serializable]
        private sealed class SaveEnvelope
        {
            public int schemaVersion = CurrentSchemaVersion;
            public string savedUtc = string.Empty;
            public CompanionProfileData profile = CompanionProfileData.CreateDefault();
        }

        public static CompanionProfileData Load(out string reason) => Load(DefaultPath, out reason);

        public static CompanionProfileData Load(string path, out string reason)
        {
            if (!TryLoad(path, out CompanionProfileData profile, out reason))
                return CompanionProfileData.CreateDefault();
            return profile;
        }

        public static bool TryLoad(string path, out CompanionProfileData profile, out string reason)
        {
            profile = CompanionProfileData.CreateDefault();
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "SavePathEmpty";
                return false;
            }
            try
            {
                if (!File.Exists(path))
                {
                    reason = "SaveMissingUsingDefaults";
                    return false;
                }

                string json = File.ReadAllText(path);
                SaveEnvelope envelope = JsonUtility.FromJson<SaveEnvelope>(json);
                if (envelope == null || envelope.profile == null)
                {
                    reason = "SaveCorruptUsingDefaults";
                    return false;
                }

                if (envelope.schemaVersion > CurrentSchemaVersion)
                {
                    reason = "SaveVersionNewerThanRuntime";
                    return false;
                }

                profile = envelope.profile.Sanitize();
                profile.SchemaVersion = CurrentSchemaVersion;
                reason = envelope.schemaVersion < CurrentSchemaVersion ? "SaveMigrated" : "SaveLoaded";
                return true;
            }
            catch (Exception exception)
            {
                reason = "SaveReadFailed:" + exception.GetType().Name;
                profile = CompanionProfileData.CreateDefault();
                return false;
            }
        }

        public static bool Save(CompanionProfileData profile, out string reason) => Save(DefaultPath, profile, out reason);

        public static bool Save(string path, CompanionProfileData profile, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "SavePathEmpty";
                return false;
            }
            try
            {
                CompanionProfileData copy = (profile ?? CompanionProfileData.CreateDefault()).Clone().Sanitize();
                copy.SchemaVersion = CurrentSchemaVersion;
                copy.LastModifiedUtc = DateTime.UtcNow.ToString("O");
                copy.Source = string.IsNullOrWhiteSpace(copy.Source) ? "runtime" : copy.Source;
                SaveEnvelope envelope = new SaveEnvelope
                {
                    schemaVersion = CurrentSchemaVersion,
                    savedUtc = copy.LastModifiedUtc,
                    profile = copy,
                };
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, JsonUtility.ToJson(envelope, true));
                AtomicReplace(temporary, path);
                reason = "SaveWritten";
                return true;
            }
            catch (Exception exception)
            {
                reason = "SaveWriteFailed:" + exception.GetType().Name;
                return false;
            }
        }

        private static void AtomicReplace(string temporary, string destination)
        {
            if (File.Exists(destination))
            {
                try
                {
                    File.Replace(temporary, destination, null);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
            }
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(temporary, destination);
        }
    }
}

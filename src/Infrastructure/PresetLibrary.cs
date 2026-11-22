using System;
using System.Collections.Generic;
using System.Linq;

namespace FanPilot
{
    internal enum PresetAction
    {
        Create,
        Update,
        Rename,
        Delete
    }

    internal static class PresetLibrary
    {
        public static IEnumerable<CurvePreset> List(Preferences preferences, CurvePreset[] personal)
        {
            var saved = preferences.CurvePresets ?? Enumerable.Empty<CurvePreset>();
            if (preferences.PersonalPresetsImported)
                return saved;
            var names = new HashSet<string>(saved.Select(preset => preset.Name), StringComparer.OrdinalIgnoreCase);
            return personal.Where(preset => names.Add(preset.Name)).ToArray().Concat(saved);
        }

        public static CurvePreset Edit(Preferences preferences, CurvePreset[] personal, PresetAction action, CurvePreset selected, string name, CurveSettings draft)
        {
            var previous = preferences.CurvePresets;
            bool wasImported = preferences.PersonalPresetsImported;
            var updated = List(preferences, personal).ToList();
            int index = updated.IndexOf(selected);
            if (action != PresetAction.Create && index < 0)
                throw new InvalidOperationException("Choose an editable preset.");
            CurvePreset result = selected;
            if (action == PresetAction.Delete)
                updated.RemoveAt(index);
            else
            {
                if (updated.Any(p => (action == PresetAction.Create || p != selected) && String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("A preset already has that name.");
                result = new CurvePreset
                {
                    Name = name,
                    Curve = (action == PresetAction.Rename ? selected.Curve : draft).Clone()
                };
                result.Validate();
                if (action == PresetAction.Create)
                {
                    if (updated.Count >= 32)
                        throw new ArgumentException("The library holds up to 32 presets.");
                    updated.Add(result);
                }
                else
                    updated[index] = result;
            }

            preferences.CurvePresets = updated;
            preferences.PersonalPresetsImported = wasImported || personal.Length > 0;
            try
            {
                preferences.Save();
            }
            catch
            {
                preferences.CurvePresets = previous;
                preferences.PersonalPresetsImported = wasImported;
                throw;
            }

            return result;
        }
    }
}

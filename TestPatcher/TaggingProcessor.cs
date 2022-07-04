using ItemTagger.ItemTypeFinder;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using System.Text.RegularExpressions;
using Mutagen.Bethesda.Plugins;
using ItemTagger.TaggingConfigs;
using Mutagen.Bethesda.Plugins.Aspects;
using Noggog;

namespace ItemTagger
{
    internal class TaggingProcessor
    {
        private TaggingConfiguration taggingConfig;

        private TaggerSettings settings;
        private IPatcherState<IFallout4Mod, IFallout4ModGetter> state;

        private ItemTyper itemTyper;

        private static readonly Regex TAG_EXTRACT_REGEX = new(@"^[\[\]()|{}]([^\[\]()|{}]+)[\[\]()|{}].+$", RegexOptions.Compiled);
        // remove leading ' - ' and following '{{{foo}}}'
        private static readonly Regex TAG_CLEAN_NAME = new(@"^\s*-\s+|\s+{{{[^{}]*}}}\s*$", RegexOptions.Compiled);

        public TaggingProcessor(
            TaggingConfiguration taggingConf,
            TaggerSettings settings,
            IPatcherState<IFallout4Mod, IFallout4ModGetter> state
        )
        {
            this.taggingConfig = taggingConf;
            this.state = state;
            this.settings = settings;

            itemTyper = new ItemTyper(state);
        }

        public void Process()
        {
            ProcessMiscs();
            ProcessKeys();
            ProcessAmmo();
            ProcessBooks();
            ProcessHolotapes();
            ProcessAlch();
        }

        private void ProcessAlch()
        {
            foreach(var alch in state.LoadOrder.PriorityOrder.Ingestible().WinningOverrides())
            {
                var prevName = alch.Name?.String;

                if (prevName.IsNullOrEmpty() || !ShouldTag(prevName))
                {
                    continue;
                }

                var curType = itemTyper.GetAlchType(alch);

                TagItem(prevName, alch, curType, state.PatchMod.Ingestibles);
            }
        }

        private void ProcessHolotapes()
        {
            foreach(var tape in state.LoadOrder.PriorityOrder.Holotape().WinningOverrides())
            {
                var prevName = tape.Name?.String;

                if (prevName.IsNullOrEmpty() || !ShouldTag(prevName))
                {
                    continue;
                }

                var curType = itemTyper.GetHolotapeType(tape);

                TagItem(prevName, tape, curType, state.PatchMod.Holotapes);
            }
        }

        private void ProcessBooks()
        {
            foreach(var book in state.LoadOrder.PriorityOrder.Book().WinningOverrides())
            {
                var prevName = book.Name?.String;

                if (prevName.IsNullOrEmpty() || !ShouldTag(prevName))
                {
                    continue;
                }

                var curType = itemTyper.GetBookType(book);

                TagItem(prevName, book, curType, state.PatchMod.Books);
            }
        }

        private void ProcessAmmo()
        {
            var ammos = state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides();
            foreach(var ammo in ammos)
            {
                var prevName = ammo.Name?.String;
                if (prevName.IsNullOrEmpty() || !ShouldTag(prevName))
                {
                    continue;
                }

                var curType = itemTyper.GetAmmoType(ammo);
                TagItem(prevName, ammo, curType, state.PatchMod.Ammunitions);
            }
        }

        private void ProcessKeys()
        {
            var keys = state.LoadOrder.PriorityOrder.Key().WinningOverrides();
            foreach(var key in keys)
            {
                var prevName = key.Name?.String;

                if (prevName.IsNullOrEmpty() || !ShouldTag(prevName))
                {
                    continue;
                }

                var curType = itemTyper.GetKeyType(key);

                TagItem(prevName, key, curType, state.PatchMod.Keys);                
            }
        }

        private void ProcessMiscs()
        {
            var miscs = state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides();

            foreach (var misc in miscs)
            {
                try
                {
                    ProcessMisc(misc);
                } 
                catch(ArgumentOutOfRangeException e)
                {
                    Console.WriteLine("Exception while processing "+misc.FormKey.ToString()+": "+e.Message+". This might be a bug in Synthesis...");
                }
            }
        }

        private void ProcessMisc(IMiscItemGetter misc)
        {
            // before even trying to process this, check if we even should
            var prevName = misc.Name?.String;
            
            if (prevName.IsNullOrEmpty() || !ShouldTag(prevName))
            {
                return;
            }

            var curType = itemTyper.GetMiscType(misc);
            if (curType != ItemType.None)
            {
                if (settings.UseComponentString && misc.Components != null && misc.Components.Count > 0)
                {
                    TagItem(prevName, misc, curType, state.PatchMod.MiscItems, misc.Components);
                }
                else
                {
                    TagItem(prevName, misc, curType, state.PatchMod.MiscItems);
                }                
            }
        }

        private void TagItem<T>(string prevName, IFallout4MajorRecordGetter item, ItemType type, Fallout4Group<T> group, IReadOnlyList<IMiscItemComponentGetter>? components)
            where T : Fallout4MajorRecord, ITranslatedNamedRequired
        {
            if (type == ItemType.None)
            {
                return;
            }

            var prefix = taggingConfig[type];
            if (prefix == "")
            {
                return;
            }

            var nameBase = TAG_CLEAN_NAME.Replace(prevName, "").Trim();

            var newItem = group.GetOrAddAsOverride(item);
            if (null != components && components.Count > 0)
            {
                newItem.Name = prefix + " " + nameBase + GetComponentString(components);
            }
            else
            {
                newItem.Name = prefix + " " + nameBase;
            }
        }

        private void TagItem<T>(string prevName, IFallout4MajorRecordGetter item, ItemType type, Fallout4Group<T> group)
            where T : Fallout4MajorRecord, ITranslatedNamedRequired
        {
            TagItem(prevName, item, type, group, null);
        }

        private string GetComponentString(IReadOnlyList<IMiscItemComponentGetter> components)
        {
            var cmpNames = components
                .Select(cmpo => cmpo.Component.TryResolve(state.LinkCache)?.Name?.String)
                .Where(kw => kw != null) ?? new List<string>();

            var joinedStr = string.Join(",", cmpNames);
            if (joinedStr == "")
            {
                return "";
            }

            return "{{{" + joinedStr + "}}}";
        }

        private static string ExtractTag(string name)
        {
            var matches = TAG_EXTRACT_REGEX.Match(name);

            if (matches.Groups.Count < 2)
            {
                return "";
            }

            return matches.Groups[1].Value;
        }

        private bool ShouldTag(string name)
        {
            var trimmedName = name.Trim();
            if(trimmedName.IsNullOrEmpty())
            {
                return false;
            }

            var existingTag = ExtractTag(trimmedName);

            if (existingTag == "")
            {
                return true;
            }

            return !taggingConfig.isTagValid(existingTag);
        }
    }
}

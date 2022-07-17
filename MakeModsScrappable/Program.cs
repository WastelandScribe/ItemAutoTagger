using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using System.Data.SqlTypes;
using Mutagen.Bethesda.Plugins.Exceptions;

namespace MakeModsScrappable
{
    public class Program
    {
        static Lazy<ModsScrappableSettings> _lazySettings = null!;
        static ModsScrappableSettings Settings => _lazySettings.Value;
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<IFallout4Mod, IFallout4ModGetter>(RunPatch)
                 .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "settings.json",
                    out _lazySettings)
                .SetTypicalOpen(GameRelease.Fallout4, "ScrappableMods.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<IFallout4Mod, IFallout4ModGetter> state)
        {
            var processor = new ModsScrapProcessor(Settings, state);
            processor.Process();
        }

        internal class ModsScrapProcessor
        {
            private ModsScrappableSettings settings;
            private IPatcherState<IFallout4Mod, IFallout4ModGetter> state;

            private readonly IEnumerable<FormKey> blackList;

            public ModsScrapProcessor(ModsScrappableSettings settings, IPatcherState<IFallout4Mod, IFallout4ModGetter> state)
            {
                this.settings = settings;
                this.state = state;

                blackList = settings.excludeList.Select(fLink => fLink.FormKey) ?? new List<FormKey>();
            }

            public void Process()
            {
                // begin with COBJs
                foreach (var cobj in state.LoadOrder.PriorityOrder.ConstructibleObject().WinningOverrides())
                {
                    try
                    {
                        ProcessCobj(cobj);
                    }
                    catch (Exception e)
                    {
                        throw RecordException.Enrich(e, cobj);
                    }
                }
            }

            private void ProcessCobj(IConstructibleObjectGetter cobj)
            {
                // TODO maybe evaluate that:
                // var numObjects = cobj.CreatedObjectCounts;
                var craftResult = cobj.CreatedObject.TryResolve(state.LinkCache);

                var omod = (craftResult as IAObjectModificationGetter);

                if (omod == null)
                {
                    return;
                }


                var miscOmod = omod.LooseMod.TryResolve(state.LinkCache);
                if (null == miscOmod)
                {
                    return;
                }

                if (cobj.Components == null)
                {
                    return;
                }

                var scrapComponents = getScrapComponents(cobj.Components);
                if (scrapComponents.Count == 0)
                {
                    return;
                }

                // otherwise, create an override fort he misc
                var newMisc = state.PatchMod.MiscItems.GetOrAddAsOverride(miscOmod);
                newMisc.Components ??= new();
                newMisc.Components.Clear();

                foreach(var pair in scrapComponents)
                {
                    var foo = new MiscItemComponent();
                    foo.Component = pair.Item1;
                    foo.Count = pair.Item2;
                    newMisc.Components.Add(foo);
                    //newMisc.Components.
                }
            }

            private List<Tuple<IFormLink<IComponentGetter>, uint>> getScrapComponents(IReadOnlyList<IConstructibleObjectComponentGetter> compList)
            {
                var result = new List<Tuple<IFormLink<IComponentGetter>, uint>>();
                foreach (var entry in compList)
                {
                    var comp = entry.Component.TryResolve(state.LinkCache);
                    double num = entry.Count;
                    if(num <= 0 || null == comp)
                    {
                        continue;
                    }

                    // is this a component?
                    if(comp is not IComponentGetter)
                    {
                        continue;
                    }
                    var compLink = comp.ToLink<IComponentGetter>();

                    if (blackList.Contains(comp.FormKey))
                    {
                        continue;
                    }
                    num = settings.componentLossFactor * num;
                    
                    switch(settings.roundMode)
                    {
                        case RoundingMode.Up:
                            num = Math.Ceiling(num);
                            break;
                        case RoundingMode.Down:
                            num = Math.Floor(num);
                            break;
                        case RoundingMode.Normal:
                            num = Math.Round(num);
                            break;
                    }
                    
                    if(num <= 0)
                    {
                        continue;
                    }
                    uint numInt = (uint)num;

                    result.Add(new Tuple<IFormLink<IComponentGetter>, uint>(compLink, numInt));
                }

                return result;
            }
        }

    }
}

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MGSC;

namespace QM_CompanyTechTiers.Patches
{
    /// <summary>
    /// Delivers this mod's config rewrites in a way that composes with other mods.
    ///
    /// The game's [Hook(ModHookType.ResourcesLoad)] affordance cannot be shared. CustomResources.Load
    /// walks the registered hooks and returns the FIRST non-null answer:
    ///
    ///     foreach (MethodInfo hook in _hooks)
    ///     {
    ///         object obj = hook.Invoke(null, _parametersCached);
    ///         if (obj != null) { ... return result; }
    ///     }
    ///     return Resources.Load(path);
    ///
    /// so whichever config-rewriting mod loads first owns a path outright and every later mod is
    /// never invoked for it - no error, no warning. This mod used that hook and therefore silently
    /// disabled any other mod that rewrote config_items, config_faction_drops, config_crafting or
    /// localization. It was equally liable to be disabled itself by a mod that loaded earlier.
    ///
    /// A postfix sidesteps the contest: it receives whatever the hooks (or Resources.Load) produced
    /// and returns it with this mod's changes applied on top, so load order stops mattering.
    /// </summary>
    [HarmonyPatch]
    public static class CustomResourcesLoadPatch
    {
        /// <summary>
        /// Resolved by hand rather than by argument types. CustomResources has both
        /// `Load(string)` and `Load&lt;T&gt;(string)`, and an argument-type array matches each of them,
        /// which Harmony reports as an AmbiguousMatchException. Patching the non-generic one is
        /// enough: Load&lt;T&gt; is a thin cast over it.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            return typeof(CustomResources)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(CustomResources.Load)
                             && !m.IsGenericMethod
                             && m.GetParameters().Length == 1
                             && m.GetParameters()[0].ParameterType == typeof(string));
        }

        public static void Postfix(string path, ref UnityEngine.Object __result)
        {
            try
            {
                __result = ResourceHook.PostProcess(path, __result);
            }
            catch (Exception ex)
            {
                // Nothing may throw out of resource loading; a failure here breaks game startup.
                Plugin.Logger.LogException(ex);
            }
        }
    }
}

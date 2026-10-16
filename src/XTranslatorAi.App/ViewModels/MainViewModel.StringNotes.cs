using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{

    /// <summary>
    /// Rows finished from a known translation without a request: a TM hit, the game's own translation, or the earlier
    /// translation kept where the English did not change. The grid gives them the TM color, as it does during a run.
    /// </summary>
    private static async Task<HashSet<long>> GetKnownTranslationIdsAsync(XTranslatorAi.Core.Data.ProjectDb db, CancellationToken cancellationToken)
    {
        var ids = new HashSet<long>();
        foreach (var kind in TranslationConstants.KnownTranslationNoteKinds)
        {
            ids.UnionWith((await db.GetStringNotesByKindAsync(kind, cancellationToken)).Keys);
        }

        return ids;
    }

    private async Task RefreshTmHitFlagsAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var hitIds = await GetKnownTranslationIdsAsync(db, cancellationToken);

        await DispatchAsync(
            () =>
            {
                foreach (var vm in Entries)
                {
                    vm.IsTranslationMemoryApplied = hitIds.Contains(vm.Id);
                }
            }
        );
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaStudio.Domain.Workflow
{
    /// <summary>A generálási és importálási munkafolyamat állapotai (spec 7.).</summary>
    public enum WorkflowState
    {
        Draft,
        Validated,
        Checked,
        Generated,
        Reviewed,
        Approved,
        ReadyToImport,
        Imported,
        Compiled,
        Saved,
        Failed,
    }

    /// <summary>
    /// Engedélyezett állapotátmenetek. Bármely hiba -> Failed, onnan csak Draft-ba
    /// (új ellenőrzés) lehet visszalépni: automatikus továbblépés tiltott.
    /// </summary>
    public static class WorkflowStateMachine
    {
        private static readonly Dictionary<WorkflowState, WorkflowState[]> Forward = new Dictionary<WorkflowState, WorkflowState[]>
        {
            { WorkflowState.Draft, new[] { WorkflowState.Validated } },
            { WorkflowState.Validated, new[] { WorkflowState.Checked } },
            { WorkflowState.Checked, new[] { WorkflowState.Generated } },
            { WorkflowState.Generated, new[] { WorkflowState.Reviewed } },
            { WorkflowState.Reviewed, new[] { WorkflowState.Approved } },
            { WorkflowState.Approved, new[] { WorkflowState.ReadyToImport } },
            { WorkflowState.ReadyToImport, new[] { WorkflowState.Imported } },
            { WorkflowState.Imported, new[] { WorkflowState.Compiled } },
            { WorkflowState.Compiled, new[] { WorkflowState.Saved } },
            { WorkflowState.Saved, new WorkflowState[0] },
            { WorkflowState.Failed, new WorkflowState[0] },
        };

        public static bool CanTransition(WorkflowState from, WorkflowState to)
        {
            if (to == WorkflowState.Draft)
            {
                return true; // modell-módosítás vagy újrakezdés mindig visszavisz DRAFT-ba
            }
            if (to == WorkflowState.Failed)
            {
                return from != WorkflowState.Failed && from != WorkflowState.Saved;
            }
            return Forward[from].Contains(to);
        }

        public static WorkflowState Transition(WorkflowState from, WorkflowState to)
        {
            if (!CanTransition(from, to))
            {
                throw new InvalidOperationException("Workflow transition " + from + " -> " + to + " is not allowed");
            }
            return to;
        }

        /// <summary>A spec szerinti nagybetűs megnevezés (pl. READY_TO_IMPORT).</summary>
        public static string DisplayName(WorkflowState state)
        {
            switch (state)
            {
                case WorkflowState.ReadyToImport:
                    return "READY_TO_IMPORT";
                default:
                    return state.ToString().ToUpperInvariant();
            }
        }
    }
}

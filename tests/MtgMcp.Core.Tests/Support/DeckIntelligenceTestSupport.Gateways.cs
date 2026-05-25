using System.Text.Json;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains fake external gateway fixtures for deck intelligence tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Provides fake Archidekt gateway behavior.
    /// </summary>
    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        /// <summary>
        /// Releases blocked fake imports once the configured import count has started.
        /// </summary>
        private readonly TaskCompletionSource<object?> importBarrier = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Coordinates fake import request recording and concurrency counters.
        /// </summary>
        private readonly object importLock = new();

        /// <summary>
        /// Tracks currently running fake imports.
        /// </summary>
        private int activeImports;

        /// <summary>
        /// Stores the largest overlapping fake import count observed.
        /// </summary>
        private int maxConcurrentImports;

        /// <summary>
        /// Counts fake imports that reached the deterministic test barrier.
        /// </summary>
        private int importBarrierStartedCount;

        /// <summary>
        /// Gets or sets the imported deck.
        /// </summary>
        public DeckWorkspace ImportedDeck { get; set; } = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        };

        /// <summary>
        /// Gets fake imported decks keyed by the caller-supplied Archidekt input.
        /// </summary>
        public Dictionary<string, DeckWorkspace> ImportedDecksByInput { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets or sets the number of started imports required before the fake import barrier releases.
        /// </summary>
        public int ReleaseImportsAfterStartedCount { get; set; }

        /// <summary>
        /// Gets the largest number of overlapping fake imports observed.
        /// </summary>
        public int MaxConcurrentImports
        {
            get
            {
                lock (importLock)
                {
                    return maxConcurrentImports;
                }
            }
        }

        /// <summary>
        /// Gets Archidekt import requests in caller order.
        /// </summary>
        public List<(string DeckIdOrUrl, bool WriteBack)> ImportRequests { get; } = [];

        /// <summary>
        /// Gets created checkpoints.
        /// </summary>
        public List<string> CreatedCheckpoints { get; } = [];

        /// <summary>
        /// Gets persisted metadata count.
        /// </summary>
        public int PersistedMetadataRequests { get; private set; }

        /// <summary>
        /// Gets persisted card mutation request count.
        /// </summary>
        public int PersistedCardRequests { get; private set; }

        /// <summary>
        /// Gets or sets the fake card persistence exception.
        /// </summary>
        public Exception? PersistCardsException { get; set; }

        /// <summary>
        /// Gets fake auth status.
        /// </summary>
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasJwt = true });
        }

        /// <summary>
        /// Lists fake decks.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>([]);
        }

        /// <summary>
        /// Creates a fake deck.
        /// </summary>
        public Task<DeckWorkspace> CreateDeckAsync(
            ArchidektDeckCreateRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckWorkspace
            {
                Name = request.Name,
                Format = request.Format,
                Description = request.Description,
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "created",
            });
        }

        /// <summary>
        /// Imports a fake deck.
        /// </summary>
        public async Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken)
        {
            lock (importLock)
            {
                ImportRequests.Add((deckIdOrUrl, writeBack));
            }

            int active = Interlocked.Increment(ref activeImports);
            try
            {
                lock (importLock)
                {
                    maxConcurrentImports = Math.Max(maxConcurrentImports, active);
                }

                if (ReleaseImportsAfterStartedCount > 0)
                {
                    await WaitForImportBarrierAsync(cancellationToken).ConfigureAwait(false);
                }

                lock (importLock)
                {
                    if (ImportedDecksByInput.TryGetValue(deckIdOrUrl, out DeckWorkspace? importedDeck))
                    {
                        DeckWorkspace cloned = CloneWorkspace(importedDeck);
                        cloned.Mode = WorkspaceMode.Archidekt;
                        cloned.WriteBack = writeBack;
                        return cloned;
                    }

                    ImportedDeck.Mode = WorkspaceMode.Archidekt;
                    ImportedDeck.WriteBack = writeBack;
                    ImportedDeck.ArchidektDeckId = "123";
                    return ImportedDeck;
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeImports);
            }
        }

        /// <summary>
        /// Waits until enough fake imports have started to prove production import overlap.
        /// </summary>
        private async Task WaitForImportBarrierAsync(CancellationToken cancellationToken)
        {
            int started = Interlocked.Increment(ref importBarrierStartedCount);
            if (started >= ReleaseImportsAfterStartedCount)
            {
                importBarrier.TrySetResult(null);
            }

            await importBarrier.Task
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a workspace so tests can mutate returned imports without changing fixtures.
        /// </summary>
        private static DeckWorkspace CloneWorkspace(DeckWorkspace workspace)
        {
            string json = JsonSerializer.Serialize(workspace);
            return JsonSerializer.Deserialize<DeckWorkspace>(json) ?? new DeckWorkspace();
        }

        /// <summary>
        /// Persists fake card changes.
        /// </summary>
        public Task PersistCardsAsync(
            DeckWorkspace workspace,
            IReadOnlyList<DeckCard> upsertedCards,
            IReadOnlyList<DeckCard> removedCards,
            CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            PersistedCardRequests++;
            if (PersistCardsException is not null)
            {
                throw PersistCardsException;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Persists a fake category.
        /// </summary>
        public Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes a fake category.
        /// </summary>
        public Task DeleteCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Persists fake metadata.
        /// </summary>
        public Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            PersistedMetadataRequests++;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> CreateCheckpointAsync(
            DeckWorkspace workspace,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            CreatedCheckpoints.Add(name);
            return Task.FromResult(new DeckCheckpoint
            {
                Id = "checkpoint-1",
                DeckId = workspace.ArchidektDeckId ?? "",
                Name = name,
                Description = description
            });
        }

        /// <summary>
        /// Lists fake checkpoints.
        /// </summary>
        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([]);
        }

        /// <summary>
        /// Gets a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> GetCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = "Checkpoint" });
        }

        /// <summary>
        /// Renames a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> RenameCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = name, Description = description });
        }

        /// <summary>
        /// Deletes a fake checkpoint.
        /// </summary>
        public Task DeleteCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

}

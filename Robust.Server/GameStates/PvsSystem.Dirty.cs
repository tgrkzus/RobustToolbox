using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Server.GameStates
{
    /// <summary>
    /// Caching for dirty bodies
    /// </summary>
    internal sealed partial class PvsSystem
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;

        /// <summary>
        /// if it's a new entity we need to GetEntityState from tick 0.
        /// </summary>
        private HashSet<EntityUid>[] _addEntities = new HashSet<EntityUid>[DirtyBufferSize];
        private HashSet<EntityUid>[] _dirtyEntities = new HashSet<EntityUid>[DirtyBufferSize];
        private int _currentIndex = 1;

        private void InitializeDirty()
        {
            for (var i = 0; i < DirtyBufferSize; i++)
            {
                _addEntities[i] = new HashSet<EntityUid>(32);
                _dirtyEntities[i] = new HashSet<EntityUid>(32);
            }
            EntityManager.EntityAdded += OnEntityAdd;
            EntityManager.EntityDirtied += OnEntityDirty;
        }

        private void ShutdownDirty()
        {
            EntityManager.EntityAdded -= OnEntityAdd;
            EntityManager.EntityDirtied -= OnEntityDirty;
        }

        private void OnEntityAdd(EntityUid e)
        {
            DebugTools.Assert(_currentIndex == _gameTiming.CurTick.Value % DirtyBufferSize ||
                _gameTiming.GetType().Name == "IGameTimingProxy");// Look I have NFI how best to excuse this assert if the game timing isn't real (a Mock<IGameTiming>).
            _addEntities[_currentIndex].Add(e);
        }

        private void OnEntityDirty(EntityUid uid)
        {
            if (!_addEntities[_currentIndex].Contains(uid))
                _dirtyEntities[_currentIndex].Add(uid);
        }

        private bool TryGetDirtyEntities(GameTick tick, [NotNullWhen(true)] out HashSet<EntityUid>? addEntities, [NotNullWhen(true)] out HashSet<EntityUid>? dirtyEntities)
        {
            var currentTick = _gameTiming.CurTick;
            if (currentTick.Value - tick.Value >= DirtyBufferSize)
            {
                addEntities = null;
                dirtyEntities = null;
                return false;
            }

            var index = tick.Value % DirtyBufferSize;
            addEntities = _addEntities[index];
            dirtyEntities = _dirtyEntities[index];
            return true;
        }

        public void CleanupDirty(IEnumerable<ICommonSession> sessions)
        {
            if (!CullingEnabled)
            {
                _seenAllEnts.Clear();
                foreach (var player in sessions)
                {
                    _seenAllEnts.Add(player);
                }
            }

            _currentIndex = ((int)_gameTiming.CurTick.Value + 1) % DirtyBufferSize;
            _addEntities[_currentIndex].Clear();
            _dirtyEntities[_currentIndex].Clear();

            foreach (var collection in _pvsCollections)
            {
                collection.ClearDirty();
            }
        }

        /// <summary>
        ///     Marks an entity's current chunk as dirty.
        /// </summary>
        internal void MarkDirty(EntityUid uid, TransformComponent xform)
        {
            var coordinates = _transform.GetMoverCoordinates(uid, xform);
            _entityPvsCollection.MarkDirty(_entityPvsCollection.GetChunkIndex(coordinates));
        }
    }
}

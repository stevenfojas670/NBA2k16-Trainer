using System.Collections.Generic;
using System.Linq;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Central registry of every cheat the trainer owns. Form.cs queries this
    /// to drive its activity log + (eventually) to render category-tabbed UI.
    /// Registration is explicit — no reflection — so behavior stays predictable
    /// across the Phase 2 cap-patch additions and the Phase 3 UI rewrite.
    /// </summary>
    internal sealed class CheatRegistry
    {
        private readonly List<Cheat> _cheats = new();

        public IReadOnlyList<Cheat> All => _cheats;

        public Cheat Register(Cheat cheat)
        {
            _cheats.Add(cheat);
            return cheat;
        }

        public IEnumerable<Cheat> ByCategory(CheatCategory category) =>
            _cheats.Where(c => c.Category == category);

        public IEnumerable<IGrouping<CheatCategory, Cheat>> Grouped() =>
            _cheats.GroupBy(c => c.Category);
    }
}

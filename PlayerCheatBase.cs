using System;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Shared lifecycle for any per-player struct cheat: probe captures the live
    /// snapshot (and the original on first probe), apply writes a desired snapshot,
    /// revert restores the captured original. Subclasses just supply <see cref="Read"/>,
    /// <see cref="Write"/>, and (when the snapshot type is mutable) <see cref="Clone"/>.
    ///
    /// Why generic over <typeparamref name="TSnapshot"/>: each cheat reads/writes a
    /// different shape (profile = record, ratings = dict, badges = bit array). The
    /// lifecycle is identical; only the IO differs.
    /// </summary>
    internal abstract class PlayerCheatBase<TSnapshot>
    {
        public TSnapshot? Original { get; protected set; }
        public TSnapshot? Live { get; protected set; }
        public bool Applied { get; protected set; }

        public abstract TSnapshot Read(ProcessSession s, IntPtr p);

        protected abstract void Write(ProcessSession s, IntPtr p, TSnapshot value);

        /// <summary>
        /// Snapshot Clone returns a copy detached from the live game state.
        /// Override for mutable snapshots (e.g. Dictionary). Records are immutable
        /// so the default identity-pass is correct.
        /// </summary>
        protected virtual TSnapshot Clone(TSnapshot value) => value;

        public TSnapshot Probe(ProcessSession s, IntPtr p)
        {
            Live = Read(s, p);
            Original ??= Clone(Live);
            return Live;
        }

        public void Apply(ProcessSession s, IntPtr p, TSnapshot desired)
        {
            if (Original is null) Probe(s, p);
            Write(s, p, desired);
            Live = Clone(desired);
            Applied = true;
        }

        public void Revert(ProcessSession s, IntPtr p)
        {
            if (Original is null) return;
            Write(s, p, Original);
            Live = Clone(Original);
            Applied = false;
        }

        public void ResetCapturedState()
        {
            Original = default;
            Live = default;
            Applied = false;
        }
    }
}

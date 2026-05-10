using System;
using System.Collections.Generic;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// One badge slot. A badge occupies <see cref="BitLength"/> consecutive bits
    /// starting at <see cref="BitStart"/> inside <c>BadgePtr[ByteOffset]</c>.
    /// 1-bit badges are on/off; 2-bit badges encode tier (0=OFF, 1=Bronze,
    /// 2=Silver, 3=Gold per the CT dropdowns).
    /// </summary>
    internal sealed record BadgeDef(string Name, int ByteOffset, int BitStart, int BitLength, string Group);

    /// <summary>
    /// Reads/writes the 83 badges mapped in NBA2k16.ct (records 5032..5120),
    /// packed across 18 bytes starting at <c>player + 0x419</c>. Lifecycle lives
    /// in <see cref="PlayerCheatBase{TSnapshot}"/>; the snapshot is keyed by
    /// badge name → tier value (0..3 for 2-bit badges, 0..1 for 1-bit).
    /// </summary>
    internal sealed class BadgesCheat : PlayerCheatBase<Dictionary<string, byte>>
    {
        // Offsets, bits, and labels extracted directly from NBA2k16.ct
        // (BadgePtr-relative; BadgePtr = player + 0x419 per the CT script).
        public static readonly BadgeDef[] Badges = new[]
        {
            new BadgeDef("Alpha dog [Exclusive with Beta dog]", 0x00, 1, 1, "Personality"),
            new BadgeDef("Beta dog [Exclusive with Alpha dog]", 0x00, 2, 1, "Personality"),
            new BadgeDef("Road dog", 0x00, 3, 1, "Personality"),
            new BadgeDef("Prime time", 0x00, 4, 1, "Personality"),
            new BadgeDef("Cool and collected", 0x00, 5, 1, "Personality"),
            new BadgeDef("Wildcard", 0x00, 6, 1, "Personality"),
            new BadgeDef("Volume shooter", 0x00, 7, 1, "Personality"),
            new BadgeDef("Closer", 0x01, 0, 1, "Personality"),
            new BadgeDef("Fierce competitor", 0x01, 1, 1, "Personality"),
            new BadgeDef("Spark plug", 0x01, 2, 1, "Personality"),
            new BadgeDef("Swagger", 0x01, 3, 1, "Personality"),
            new BadgeDef("Mind games", 0x01, 4, 1, "Personality"),
            new BadgeDef("Enforcer", 0x01, 5, 1, "Personality"),
            new BadgeDef("Championship DNA", 0x01, 6, 1, "Personality"),
            new BadgeDef("Mentor", 0x01, 7, 1, "Personality"),
            new BadgeDef("Hearth and soul", 0x02, 0, 1, "Personality"),
            new BadgeDef("Floor general", 0x02, 1, 1, "Personality"),
            new BadgeDef("Defensive anchor", 0x02, 2, 1, "Personality"),
            new BadgeDef("Hardenend", 0x02, 3, 1, "Personality"),
            new BadgeDef("Gym rat", 0x02, 4, 2, "Personality"),
            new BadgeDef("Reserved [Exclusive with Friendly]", 0x02, 6, 1, "Personality"),
            new BadgeDef("Friendly [Exclusive with Reserved]", 0x02, 7, 1, "Personality"),
            new BadgeDef("Low ego [Exclusive with All-Time great]", 0x03, 0, 1, "Personality"),
            new BadgeDef("All-Time great [Exclusive with Low ego]", 0x03, 1, 1, "Personality"),
            new BadgeDef("High work ethic [Exclusive with Legendary work ethic]", 0x03, 2, 1, "Personality"),
            new BadgeDef("Legendary work ethic [Exclusive with High work ethic]", 0x03, 3, 1, "Personality"),
            new BadgeDef("Keep it real [Exclusive with Pat my back]", 0x03, 4, 1, "Personality"),
            new BadgeDef("Pat my back [Exclusive with Keep it real]", 0x03, 5, 1, "Personality"),
            new BadgeDef("Expressive [Exclusive with Unpredictable/Laid back]", 0x03, 6, 1, "Personality"),
            new BadgeDef("Unpredictable [Exclusive with Expressive/Laid back]", 0x03, 7, 1, "Personality"),
            new BadgeDef("Laid back [Exclusive with Expressive/Unpredictable]", 0x04, 0, 1, "Personality"),
            new BadgeDef("Microwave", 0x04, 1, 2, "Personality"),
            new BadgeDef("Unfazed", 0x04, 3, 2, "Personality"),
            new BadgeDef("Corner Specialist", 0x04, 5, 2, "Personality"),
            new BadgeDef("Deadeye", 0x05, 0, 2, "Personality"),
            new BadgeDef("Limitless Range", 0x05, 2, 2, "Personality"),
            new BadgeDef("Fade Ace", 0x05, 4, 2, "Personality"),
            new BadgeDef("Shot Creator", 0x05, 6, 2, "Personality"),
            new BadgeDef("Lob City Finisher", 0x06, 0, 2, "Skill"),
            new BadgeDef("Posterizer", 0x06, 2, 2, "Skill"),
            new BadgeDef("Spin Lay-In", 0x06, 4, 2, "Skill"),
            new BadgeDef("Hot-Stepper", 0x06, 6, 2, "Skill"),
            new BadgeDef("King of Euros", 0x07, 0, 2, "Skill"),
            new BadgeDef("Acrobat", 0x07, 2, 2, "Skill"),
            new BadgeDef("Tear Dropper", 0x07, 4, 2, "Skill"),
            new BadgeDef("Hustle Points", 0x07, 6, 2, "Skill"),
            new BadgeDef("Screen Outlet", 0x08, 0, 2, "Skill"),
            new BadgeDef("Bank is Open", 0x08, 2, 2, "Skill"),
            new BadgeDef("Relentless Finisher", 0x08, 4, 2, "Skill"),
            new BadgeDef("Post Spin Technician", 0x08, 6, 2, "Skill"),
            new BadgeDef("Drop-Stepper", 0x09, 0, 2, "Skill"),
            new BadgeDef("Post Hoperator", 0x09, 2, 2, "Skill"),
            new BadgeDef("Post Stepback Pro", 0x09, 4, 2, "Skill"),
            new BadgeDef("Dream-Like Up and Under", 0x09, 6, 2, "Skill"),
            new BadgeDef("Post Hook Specialist", 0x0A, 0, 2, "Skill"),
            new BadgeDef("Killer Crossover", 0x0A, 2, 2, "Skill"),
            new BadgeDef("Spin Kingpin", 0x0A, 4, 2, "Skill"),
            new BadgeDef("Stepback Freeze", 0x0A, 6, 2, "Skill"),
            new BadgeDef("Behind the back Pro", 0x0B, 0, 2, "Skill"),
            new BadgeDef("Hestitation Stunner", 0x0B, 2, 2, "Skill"),
            new BadgeDef("Master of In and Out", 0x0B, 4, 2, "Skill"),
            new BadgeDef("Pet move size-up", 0x0B, 6, 2, "Skill"),
            new BadgeDef("Flashy Passer", 0x0C, 0, 2, "Skill"),
            new BadgeDef("Break Starter", 0x0C, 2, 2, "Skill"),
            new BadgeDef("Pick & Roll Maestro", 0x0C, 4, 2, "Skill"),
            new BadgeDef("Lob City Passer", 0x0C, 6, 2, "Skill"),
            new BadgeDef("Dimer", 0x0D, 0, 2, "Defense/Utility"),
            new BadgeDef("On Court Coach", 0x0D, 2, 1, "Defense/Utility"),
            new BadgeDef("Scrapper", 0x0D, 3, 2, "Defense/Utility"),
            new BadgeDef("Offensive Crasher", 0x0D, 5, 2, "Defense/Utility"),
            new BadgeDef("Defensive Crasher", 0x0E, 0, 2, "Defense/Utility"),
            new BadgeDef("Perimeter Lockdown Defender", 0x0E, 2, 2, "Defense/Utility"),
            new BadgeDef("Post Lockdown Defender", 0x0E, 4, 2, "Defense/Utility"),
            new BadgeDef("Charge Card", 0x0E, 6, 2, "Defense/Utility"),
            new BadgeDef("Pick Dodger", 0x0F, 0, 2, "Defense/Utility"),
            new BadgeDef("Interceptor", 0x0F, 2, 2, "Defense/Utility"),
            new BadgeDef("Pick Pocket", 0x0F, 4, 2, "Defense/Utility"),
            new BadgeDef("Eraser", 0x0F, 6, 2, "Defense/Utility"),
            new BadgeDef("Chase Down Artist", 0x10, 0, 2, "Defense/Utility"),
            new BadgeDef("Bruiser", 0x10, 2, 2, "Defense/Utility"),
            new BadgeDef("Brick Wall", 0x10, 4, 2, "Defense/Utility"),
            new BadgeDef("One Man Fast Break", 0x10, 6, 2, "Defense/Utility"),
            new BadgeDef("Transition Finisher", 0x11, 0, 2, "Defense/Utility"),
        };

        public static byte MaxTierFor(BadgeDef b) => (byte)((1 << b.BitLength) - 1);

        public override Dictionary<string, byte> Read(ProcessSession s, IntPtr p)
        {
            IntPtr badgeBase = PlayerStructIO.BadgeBase(p);

            // Coalesce reads: every badge in the same byte hits the same address,
            // so read each unique byte once.
            var byteCache = new Dictionary<int, byte>();
            var dict = new Dictionary<string, byte>(Badges.Length);
            foreach (var b in Badges)
            {
                if (!byteCache.TryGetValue(b.ByteOffset, out byte raw))
                {
                    raw = s.ReadByte(new IntPtr(badgeBase.ToInt64() + b.ByteOffset));
                    byteCache[b.ByteOffset] = raw;
                }
                int mask = (1 << b.BitLength) - 1;
                dict[b.Name] = (byte)((raw >> b.BitStart) & mask);
            }
            return dict;
        }

        protected override void Write(ProcessSession s, IntPtr p, Dictionary<string, byte> values)
        {
            IntPtr badgeBase = PlayerStructIO.BadgeBase(p);

            // Group badges by byte and do a single read-modify-write per byte,
            // otherwise we'd clobber the in-flight edit when two badges share a
            // byte (which happens for all 83 entries — they pack 4-8 per byte).
            var byBytes = new Dictionary<int, List<BadgeDef>>();
            foreach (var b in Badges)
            {
                if (!byBytes.TryGetValue(b.ByteOffset, out var list))
                {
                    list = new List<BadgeDef>();
                    byBytes[b.ByteOffset] = list;
                }
                list.Add(b);
            }

            foreach (var (off, list) in byBytes)
            {
                IntPtr addr = new IntPtr(badgeBase.ToInt64() + off);
                byte raw = s.ReadByte(addr);
                foreach (var b in list)
                {
                    if (!values.TryGetValue(b.Name, out byte v)) continue;
                    int mask = (1 << b.BitLength) - 1;
                    int clamped = v & mask;
                    raw = (byte)((raw & ~(mask << b.BitStart)) | (clamped << b.BitStart));
                }
                s.WriteByte(addr, raw);
            }
        }

        // Dict is mutable; defensive copy so callers can't poke Original/Live.
        protected override Dictionary<string, byte> Clone(Dictionary<string, byte> value) =>
            new Dictionary<string, byte>(value);
    }
}

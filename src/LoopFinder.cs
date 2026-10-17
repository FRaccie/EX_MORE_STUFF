// Suggests where a song should loop. A song for the game plays its intro once and then repeats one stretch forever,
// so the jump from the loop end back to the loop start must land on matching music. The finder looks for the
// longest stretch of the song that comes back later (a soundtrack rip that plays its loop twice comes back whole):
// it compares the song with itself at every delay of 10 s or more, on a rough sound fingerprint (the loudness of six
// pitch bands every 46 ms), then finds the exact delay sample by sample, and puts the loop start on a zero crossing.
using System;
using System.Collections.Generic;

namespace ExMoreStuff
{
    sealed class LoopSuggestion
    {
        public int Start, End;
        public double Match;           // 0..1: how alike the music is either side of the seam
        public bool Repeat;            // false: nothing in the song comes back, so the whole song loops
    }

    static class LoopFinder
    {
        const int Hop = 2048, Bands = 6;
        const double MinLoopSeconds = 10, Window = 4;   // a loop of 10 s at least; matches judged over 4 s

        public static LoopSuggestion Find(short[] pcm) { return FindAll(pcm)[0]; }

        /// <summary>Every loop found, the best first (Find's pick; up to 6): Advanced > Music's Next loop steps through
        /// them (user 2026-10-16: "check next possible loop").</summary>
        public static List<LoopSuggestion> FindAll(short[] pcm)
        {
            int frames = pcm.Length / 2;
            int end = LastSound(pcm);
            var f = Fingerprint(pcm, end);
            int n = f.Length / Bands, minLag = (int)(MinLoopSeconds * Song.Rate / Hop), win = Math.Max(1, (int)(Window * Song.Rate / Hop));
            // a moment counts as music when it's within 40 dB of the song's loudest
            var level = new double[n];
            double loudest = -200;
            for (int t = 0; t < n; t++)
            {
                double p = 0;
                for (int b = 0; b < Bands; b++) p += Math.Pow(10, f[t * Bands + b] / 10);
                level[t] = 10 * Math.Log10(p + 1e-12);
                loudest = Math.Max(loudest, level[t]);
            }
            var loud = new bool[n];
            for (int t = 0; t < n; t++) loud[t] = level[t] > loudest - 40;

            // what unrelated moments differ by (the typical distance), so "alike" means far below it
            var sample = new List<double>();
            var rng = new Random(1);
            for (int i = 0; i < 4000 && n > 2; i++)
            {
                int a = rng.Next(n), b = rng.Next(n);
                if (Math.Abs(a - b) > win) sample.Add(Dist(f, a, b));
            }
            sample.Sort();
            double typical = sample.Count > 0 ? sample[sample.Count / 2] : 1, limit = 0.2 * typical;

            // every delay's longest matching run; the strongest few (at least a second apart) become candidates
            var runs = new List<int[]>();       // lag, run, run start
            var d = new double[n + 1];
            for (int lag = minLag; lag < n - win; lag++)
            {
                int m = n - lag;
                d[0] = 0;
                for (int t = 0; t < m; t++) d[t + 1] = d[t] + (loud[t] || loud[t + lag] ? Dist(f, t, t + lag) : limit * 2);
                // longest run of moments whose surrounding 4 s match
                int run = 0, runStart = 0, best = 0, bestStart = 0;
                for (int t = 0; t + win <= m; t++)
                {
                    if ((d[t + win] - d[t]) / win < limit)
                    {
                        if (run == 0) runStart = t;
                        if (++run > best) { best = run; bestStart = runStart; }
                    }
                    else run = 0;
                }
                if (best * Hop >= 2 * Song.Rate) runs.Add(new[] { lag, best, bestStart });
            }
            runs.Sort((a, b) => b[1].CompareTo(a[1]));
            var candidates = new List<int[]>();
            int second = Song.Rate / Hop;
            foreach (var r in runs)
            {
                if (candidates.Count >= 6) break;
                if (candidates.TrueForAll(c => Math.Abs(c[0] - r[0]) > second)) candidates.Add(r);
            }
            if (candidates.Count == 0)
                return new List<LoopSuggestion> { new LoopSuggestion { Start = 0, End = Math.Max(Adx.FrameSamples * 4, end), Match = SeamMatch(pcm, 0, end), Repeat = false } };

            // each candidate made exact on the waveform; the best seam first, longer loops winning near-ties (the same
            // loop found twice: kept once)
            var found = new List<KeyValuePair<double, LoopSuggestion>>();
            foreach (var c in candidates)
            {
                var s = Exact(pcm, c[0] * Hop, c[2] * Hop, c[1] * Hop);
                double value = s.Match + 0.15 * Math.Min(1.0, (s.End - s.Start) / (60.0 * Song.Rate));
                if (found.TrueForAll(k => Math.Abs(k.Value.Start - s.Start) > Song.Rate / 10 || Math.Abs(k.Value.End - s.End) > Song.Rate / 10))
                    found.Add(new KeyValuePair<double, LoopSuggestion>(value, s));
            }
            found.Sort((a, b) => b.Key.CompareTo(a.Key));
            return found.ConvertAll(k => k.Value);
        }

        // a candidate (delay, matching run) to sample accuracy: the delay from the waveform, the start at the earliest
        // point, every 1/4 s along the run, where the waveforms really match (music that only sounds alike, like an
        // intro playing the loop's end differently, passes the fingerprint), then the delay fitted again right at
        // that seam and the start put on a zero crossing
        static LoopSuggestion Exact(short[] pcm, int coarse, int runStart, int runLength)
        {
            int frames = pcm.Length / 2;
            int start = Math.Min(frames - 1, runStart + Song.Rate);
            int span = Math.Max(1, runLength - Song.Rate);
            int length = RefineLength(pcm, start, coarse, Hop + Hop / 2, span);
            var scores = new List<KeyValuePair<int, double>>();
            for (int s = start; s < start + span && s + length + Song.Rate / 5 < frames; s += Song.Rate / 4)
                scores.Add(new KeyValuePair<int, double>(s, SeamMatch(pcm, s, s + length)));
            if (scores.Count > 0)
            {
                double top = 0;
                foreach (var kv in scores) top = Math.Max(top, kv.Value);
                foreach (var kv in scores) if (kv.Value >= top - 0.03) { start = kv.Key; break; }
            }
            start = ZeroCrossing(pcm, start, Song.Rate / 50);
            length = RefineLength(pcm, start, length, Song.Rate / 50, Song.Rate / 2);
            int stop = Math.Min(frames, start + length);
            return new LoopSuggestion { Start = start, End = stop, Match = SeamMatch(pcm, start, stop), Repeat = true };
        }

        /// <summary>The loop end nearest after `from` (`forward`) or before it where the music matches what follows `start`
        /// (Advanced > Music's Next match / Previous match, user 2026-10-16: "the user puts the green slider where they want
        /// the loop to be, then the orange slider can be ticked to find next closest match"); -1 when there's none. Found
        /// on the sound fingerprint (up to 4 s after each side compared, every 46 ms), then made exact on the waveform.</summary>
        public static int MatchEnd(short[] pcm, int start, int from, bool forward)
        {
            int frames = pcm.Length / 2;
            var f = Fingerprint(pcm, frames);
            int n = f.Length / Bands, win = Math.Max(1, (int)(Window * Song.Rate / Hop)), least = Math.Max(1, Song.Rate / Hop), s0 = start / Hop;
            if (s0 + least >= n) return -1;
            // what unrelated moments differ by, so "alike" means well below it
            var sample = new List<double>();
            var rng = new Random(1);
            for (int i = 0; i < 4000 && n > 2; i++)
            {
                int a = rng.Next(n), b = rng.Next(n);
                if (Math.Abs(a - b) > win) sample.Add(Dist(f, a, b));
            }
            sample.Sort();
            double typical = sample.Count > 0 ? sample[sample.Count / 2] : 1, limit = 0.25 * typical;
            // how alike what follows the start and what follows each end are (at least 1 s of each; up to 4 s)
            var alike = new double[n];
            for (int t = 0; t < n; t++) alike[t] = double.MaxValue;
            for (int t = s0 + least; t + least <= n; t++)
            {
                int w = Math.Min(win, Math.Min(n - t, n - s0));
                double sum = 0;
                for (int k = 0; k < w; k++) sum += Dist(f, s0 + k, t + k);
                alike[t] = sum / w;
            }
            // the matches: stretches where it's alike, each its best moment (one spot, not several 46 ms apart), at least half a
            // second past the orange line either way
            var spots = new List<int>();
            for (int t = s0 + least; t < n; t++)
            {
                if (alike[t] >= limit) continue;
                int best = t;
                while (t + 1 < n && alike[t + 1] < limit) { t++; if (alike[t] < alike[best]) best = t; }
                if (spots.Count > 0 && best - spots[spots.Count - 1] < least / 2) { if (alike[best] < alike[spots[spots.Count - 1]]) spots[spots.Count - 1] = best; }
                else spots.Add(best);
            }
            int pick = -1, at = from / Hop, half = least / 2;
            foreach (int t in spots)
            {
                if (forward && t > at + half) { pick = t; break; }
                if (!forward && t < at - half) pick = t;
            }
            if (pick < 0) return -1;
            // to the sample: the loop length lined up on the waveform, then the end fitted to the start
            int length = RefineLength(pcm, start, pick * Hop - start, Hop + Hop / 2, (int)(Window * Song.Rate));
            int end = Math.Min(frames, start + length);
            try { end = FitEnd(pcm, start, end, Song.Rate / 50); } catch (Exception) { }
            return end;
        }

        /// <summary>The loop end within ±`reach` frames of `end` whose music best continues into `start`.</summary>
        public static int FitEnd(short[] pcm, int start, int end, int reach)
        {
            int frames = pcm.Length / 2;
            int length = RefineLength(pcm, start, end - start, reach, Song.Rate / 2);
            return Math.Max(start + 1, Math.Min(frames, start + length));
        }

        /// <summary>0..1: how alike the 0.2 s after the loop end (as the song has it) and after the loop start are, and
        /// how small the step at the seam is.</summary>
        public static double SeamMatch(short[] pcm, int start, int end)
        {
            int frames = pcm.Length / 2, n = Math.Min(Song.Rate / 5, frames - Math.Max(start, end));
            // a loop end at the song's end: compare what leads up to each side instead
            int back = 0;
            if (n < Song.Rate / 50) { back = Math.Min(Song.Rate / 5, Math.Min(start, end)); n = back; }
            double diff = 0, energy = 0;
            for (int i = 0; i < n; i++)
                for (int c = 0; c < 2; c++)
                {
                    double x = pcm[(start - back + i) * 2 + c], y = pcm[(end - back + i) * 2 + c];
                    diff += (x - y) * (x - y);
                    energy += x * x + y * y;
                }
            // nothing to compare either side (a loop of the whole song): only the step counts
            double alike = n <= 0 ? 0.5 : energy > 0 ? Math.Max(0, 1 - 2 * diff / energy) : 1;
            // the step heard at the jump: the sample before the end against the one the song has before the start
            double step = 0;
            if (end > 0 && start > 0 && start < frames)
                for (int c = 0; c < 2; c++) step = Math.Max(step, Math.Abs(pcm[(end - 1) * 2 + c] - pcm[(start - 1) * 2 + c]));
            double smooth = 1 - Math.Min(1, step / 6000.0);
            return alike * (0.8 + 0.2 * smooth);
        }

        // -- the parts -------------------------------------------------------------------------------------------------
        // the last frame louder than near-silence (a song's trailing silence isn't looped)
        static int LastSound(short[] pcm)
        {
            int frames = pcm.Length / 2;
            for (int f = frames - 1; f > 0; f--)
                if (Math.Abs((int)pcm[f * 2]) > 64 || Math.Abs((int)pcm[f * 2 + 1]) > 64) return Math.Min(frames, f + 1);
            return frames;
        }

        // log loudness of six bands (split by one-pole low-passes at 150, 400, 1000, 2500, 6000 Hz) per hop
        static float[] Fingerprint(short[] pcm, int frames)
        {
            int n = frames / Hop;
            var f = new float[n * Bands];
            double[] cut = { 150, 400, 1000, 2500, 6000 };
            var k = new double[cut.Length];
            for (int i = 0; i < cut.Length; i++) k[i] = 1 - Math.Exp(-2 * Math.PI * cut[i] / Song.Rate);
            var lp = new double[cut.Length];
            var sum = new double[Bands];
            for (int t = 0; t < n; t++)
            {
                Array.Clear(sum, 0, Bands);
                for (int s = t * Hop; s < (t + 1) * Hop; s++)
                {
                    double x = (pcm[s * 2] + pcm[s * 2 + 1]) * (0.5 / 32768.0), below = x;
                    for (int i = 0; i < cut.Length; i++) lp[i] += k[i] * (x - lp[i]);
                    // band 0: under 150 Hz; band i: between cut[i-1] and cut[i]; band 5: above 6 kHz
                    sum[0] += lp[0] * lp[0];
                    for (int i = 1; i < cut.Length; i++) { double b = lp[i] - lp[i - 1]; sum[i] += b * b; }
                    double top = x - lp[cut.Length - 1];
                    sum[Bands - 1] += top * top;
                }
                for (int b = 0; b < Bands; b++) f[t * Bands + b] = (float)(10 * Math.Log10(sum[b] / Hop + 1e-9));
            }
            return f;
        }

        static double Dist(float[] f, int a, int b)
        {
            double s = 0;
            for (int i = 0; i < Bands; i++) { double x = f[a * Bands + i] - f[b * Bands + i]; s += x * x; }
            return s;
        }

        // the loop length within ±reach of `guess` where the waveform from `start` best lines up with the one a loop
        // later (normalised cross-correlation of the mono mix over a few spots spread over `span` frames)
        static int RefineLength(short[] pcm, int start, int guess, int reach, int span)
        {
            int frames = pcm.Length / 2;
            const int Piece = 8192;
            var spots = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int s = start + (int)((long)i * Math.Max(0, span - Piece) / 4);
                if (s >= 0 && s + guess + reach + Piece < frames) spots.Add(s);
            }
            if (spots.Count == 0)
            {
                // too near the end for spots after the start: line up what leads into each side instead
                int s = Math.Max(reach, start - Piece);
                if (s + guess + reach + Piece < frames || s < 0) spots.Add(Math.Max(0, Math.Min(s, frames - guess - reach - Piece - 1)));
                // nothing that fits to line up: keep the guess
                if (spots.Count == 0 || spots[0] + guess - reach < 0 || spots[0] + guess + reach + Piece >= frames) return guess;
            }
            var mono = new float[frames];
            for (int i = 0; i < frames; i++) mono[i] = pcm[i * 2] + pcm[i * 2 + 1];
            int best = guess;
            double bestScore = double.MinValue;
            for (int lag = guess - reach; lag <= guess + reach; lag++)
            {
                if (lag <= 0) continue;
                double score = 0;
                foreach (int s in spots)
                {
                    if (s + lag + Piece > frames) { score = double.MinValue; break; }
                    double xy = 0, xx = 0, yy = 0;
                    for (int i = 0; i < Piece; i += 2)
                    {
                        double x = mono[s + i], y = mono[s + lag + i];
                        xy += x * y; xx += x * x; yy += y * y;
                    }
                    score += xy / Math.Sqrt(xx * yy + 1);
                }
                if (score > bestScore) { bestScore = score; best = lag; }
            }
            // the coarse pass skipped every other sample: settle on the best neighbour
            for (int lag = best - 1; lag <= best + 1; lag += 2)
            {
                double score = 0;
                foreach (int s in spots)
                {
                    if (s + lag + Piece > frames || lag <= 0) { score = double.MinValue; break; }
                    double xy = 0, xx = 0, yy = 0;
                    for (int i = 0; i < Piece; i++) { double x = mono[s + i], y = mono[s + lag + i]; xy += x * y; xx += x * x; yy += y * y; }
                    score += xy / Math.Sqrt(xx * yy + 1);
                }
                double here = 0;
                foreach (int s in spots)
                {
                    double xy = 0, xx = 0, yy = 0;
                    for (int i = 0; i < Piece; i++) { double x = mono[s + i], y = mono[s + best + i]; xy += x * y; xx += x * x; yy += y * y; }
                    here += xy / Math.Sqrt(xx * yy + 1);
                }
                if (score > here) best = lag;
            }
            return best;
        }

        /// <summary>A copy of the song whose last `fade` frames before the loop end blend into what the song plays just
        /// before the loop start, so the jump back lands mid-phrase on the same sound (a straight crossfade).</summary>
        public static short[] SmoothSeam(short[] pcm, int start, int end, int fade)
        {
            fade = Math.Min(fade, Math.Min(start, end - start));
            var o = (short[])pcm.Clone();
            if (fade < 2) return o;
            for (int i = 0; i < fade; i++)
            {
                // a straight blend: the two sides of a good seam are nearly the same sound, which an equal-power blend
                // would make up to 3 dB louder halfway through
                double t = (i + 0.5) / fade, keep = 1 - t, take = t;
                int at = end - fade + i, from = start - fade + i;
                for (int c = 0; c < 2; c++)
                {
                    double v = pcm[at * 2 + c] * keep + pcm[from * 2 + c] * take;
                    o[at * 2 + c] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(v)));
                }
            }
            return o;
        }

        /// <summary>The frame near `f` (within `reach`) where the mix crosses zero, quietest first.</summary>
        public static int ZeroCrossing(short[] pcm, int f, int reach)
        {
            int frames = pcm.Length / 2, best = f;
            double bestScore = double.MaxValue;
            for (int d = -reach; d <= reach; d++)
            {
                int g = f + d;
                if (g <= 0 || g >= frames) continue;
                int now = pcm[g * 2] + pcm[g * 2 + 1], before = pcm[(g - 1) * 2] + pcm[(g - 1) * 2 + 1];
                double score = Math.Abs(now) + (((now < 0) != (before < 0)) ? 0 : 8000) + Math.Abs(d) * 0.5;
                if (score < bestScore) { bestScore = score; best = g; }
            }
            return best;
        }
    }
}

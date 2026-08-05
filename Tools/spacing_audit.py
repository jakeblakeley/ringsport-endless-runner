"""
Port of ObstacleSpawner's spacing model. Measures, per level, the TIME gap
between consecutive obstacle rows and flags any gap that lands under the
action budget its row demands. Run before/after with STRETCH/FLOOR toggled.
"""
import random, sys, statistics

REACTION = 0.6
LANE_MECH = 0.30
JUMP_MECH = 0.05
COOLDOWN = 0.20

def chain(lane_changes, needs_jump):
    g = lane_changes + (1 if needs_jump else 0)
    if g == 0:
        return 0.0
    return REACTION + lane_changes*LANE_MECH + (JUMP_MECH if needs_jump else 0.0) + (g-1)*COOLDOWN

SAME_LANE = chain(0, True)       # 0.65
LANE_CHANGE = chain(1, False)    # 0.90
FORCING_ROW = chain(1, True)     # 1.15
PATTERN_TAIL = LANE_CHANGE       # 0.90
PIN_CHAIN = chain(2, True)       # 1.65
PALISADE_REC = REACTION + 0.70   # 1.30
MIN_GAP_TIME = LANE_CHANGE

PASSABLE = {"ObstacleJump", "ObstaclePalisade", "ObstacleBroadJump"}

# levelNumber: (runSpeed, minSpacing, maxSpacing, patternUsageRatio, minDiff, maxDiff, duration)
NEW_LEVELS = {
    1: (15, 17.5, 27,   0.50, 1, 3,  30),
    2: (15, 16.5, 25.5, 0.60, 1, 4,  20),
    3: (15, 15.5, 24,   0.70, 2, 5,  60),
    4: (16, 16,   24,   0.70, 3, 6,  90),
    5: (16, 15.5, 23,   0.75, 4, 7,  90),
    6: (18, 17,   25,   0.75, 5, 8, 120),
    7: (18, 16.5, 24,   0.80, 6, 9, 120),
    8: (20, 18,   26,   0.85, 7, 10, 180),
}
OLD_LEVELS = {
    1: (15, 12, 20, 0.50, 1, 3,  30),
    2: (15, 11, 18, 0.60, 1, 4,  20),
    3: (15, 10, 16, 0.70, 2, 5,  60),
    4: (16,  9, 15, 0.70, 3, 6,  90),
    5: (16,  9, 14, 0.75, 4, 7,  90),
    6: (18, 10, 14, 0.75, 5, 8, 120),
    7: (18, 10, 14, 0.80, 6, 9, 120),
    8: (20, 11, 15, 0.85, 7, 10, 180),
}
OLD_TIMES = dict(same=0.45, forcing=0.9, tail=0.55, pin=1.4, pal=1.1)

# name, difficulty, minLevel, maxLevel, patternLength, [(type, lane, zOffset)]
PATTERNS = [
 ("Easy Zigzag", 2, 1, 4, 25, [("ObstacleJump",-1,0),("ObstacleJump",0,8),("ObstacleJump",1,16)]),
 ("Easy Straight Line", 1, 1, 3, 30, [("ObstacleJump",0,0),("ObstacleJump",0,10),("ObstacleJump",0,20)]),
 ("Easy Alternate", 2, 1, 4, 28, [("ObstacleJump",-1,0),("ObstacleJump",1,10),("ObstacleJump",-1,20)]),
 ("Easy Gap", 3, 2, 5, 20, [("ObstacleAvoid",-1,0),("ObstacleAvoid",1,0)]),
 ("Medium Double Jump", 4, 3, 6, 24, [("ObstacleJump",0,0),("ObstacleJump",0,8),("ObstacleAvoid",0,16)]),
 ("Medium Slalom", 5, 3, 7, 30, [("ObstaclePylon",-1,0),("ObstaclePylon",1,7),("ObstaclePylon",-1,14),("ObstaclePylon",1,21)]),
 ("Medium Mixed Row", 5, 4, 7, 15, [("ObstacleAvoid",-1,0),("ObstacleJump",0,0),("ObstaclePylon",1,0)]),
 ("Medium Palisade Intro", 6, 4, 8, 20, [("ObstacleJump",0,0),("ObstaclePalisade",0,10)]),
 ("Hard Rapid Fire", 7, 6, 9, 40, [("ObstacleJump",-1,0),("ObstacleJump",0,10),("ObstacleJump",1,20),("ObstacleJump",0,30)]),
 ("Hard Triple Row", 7, 6, 9, 22, [("ObstacleAvoid",-1,0),("ObstacleJump",0,0),("ObstacleAvoid",1,0),("ObstaclePylon",0,14)]),
 ("Hard Narrow Window", 8, 7, 9, 22, [("ObstaclePylon",-1,0),("ObstaclePylon",1,0),("ObstacleJump",0,8),("ObstaclePylon",-1,15),("ObstaclePylon",1,15)]),
 ("Hard Broad Jump Challenge", 8, 6, 9, 25, [("ObstacleBroadJump",0,0),("ObstacleJump",-1,10),("ObstacleBroadJump",1,15)]),
 ("Expert Gauntlet", 9, 7, 9, 55, [("ObstacleAvoid",-1,0),("ObstacleAvoid",1,0),("ObstacleJump",0,8),("ObstacleJump",0,16),
                                   ("ObstaclePalisade",-1,36),("ObstacleAvoid",0,36),("ObstacleAvoid",1,36),("ObstaclePylon",0,48)]),
 ("Expert Palisade Gauntlet", 10, 8, 9, 30, [("ObstaclePalisade",0,0),("ObstaclePylon",-1,8),("ObstaclePylon",1,8),("ObstacleJump",0,22)]),
]


def group_rows(obstacles):
    rows = {}
    for t, lane, z in obstacles:
        rows.setdefault(round(z, 2), []).append((t, lane, z))
    return [(k, rows[k]) for k in sorted(rows)]


def lane_free(row, lane):
    return not any(l == lane for _, l, _ in row)

def lane_survivable(row, lane):
    for t, l, _ in row:
        if l == lane:
            return t in PASSABLE
    return True

def transition_cost(frm, to, row):
    return chain(abs(to - frm), not lane_free(row, to))


def row_lead(from_mask, row, times):
    """Returns (cost, to_mask). Mirrors ObstacleSpawner.RowLeadTime."""
    best = float('inf')
    for frm in (-1, 0, 1):
        if not (from_mask >> (frm + 1)) & 1:
            continue
        for to in (-1, 0, 1):
            if lane_survivable(row, to):
                best = min(best, transition_cost(frm, to, row))
    if best == float('inf'):
        return times['forcing'], 0b111
    lead = max(best, times['forcing']) if len(row) >= 2 else best
    m = 0
    for frm in (-1, 0, 1):
        if not (from_mask >> (frm + 1)) & 1:
            continue
        for to in (-1, 0, 1):
            if lane_survivable(row, to) and transition_cost(frm, to, row) <= lead + 1e-4:
                m |= 1 << (to + 1)
    return lead, m


def required_row_lead(prev, row, times):
    """Audit-side check: cost of meeting `row` from wherever `prev` left the player."""
    _, mask = row_lead(0b111, prev, times)
    cost, _ = row_lead(mask, row, times)
    return cost


def stretch_rows(rows, length, speed, times, enabled, start_mask=0b111):
    """Returns [(offset, row)], stretchedLength, endMask."""
    if not enabled:
        m = start_mask
        for _, row in rows:
            _, m = row_lead(m, row, times)
        return rows, length, m
    out, a_prev, s_prev, first, mask = [], 0.0, 0.0, True, start_mask
    for off_a, row in rows:
        req, mask = row_lead(mask, row, times)
        req *= speed
        off = off_a if first else s_prev + max(off_a - a_prev, req)
        out.append((off, row))
        a_prev, s_prev, first = off_a, off, False
    return out, length + (s_prev - a_prev), mask


def simulate(level, cfg, times, stretch, floor, seed):
    rng = random.Random(seed)
    speed, mn, mx, ratio, mind, maxd, duration = cfg
    lane_time = times['lane']

    valid = [p for p in PATTERNS if p[2] <= level <= p[3] and mind <= p[1] <= maxd]

    z = 20.0
    mask = 0b111
    last_obstacle_z = -100.0
    last_forcing_z = -100.0
    last_palisade_z = -100.0
    events = []       # (z, rowlist)
    horizon = duration * speed

    def gap():
        g = rng.uniform(mn, mx)
        return max(g, MIN_GAP_TIME * speed) if floor else g

    def spawn_floor():
        return max(last_forcing_z + times['forcing'] * speed,
                   last_palisade_z + times['pal'] * speed)

    guard = 0
    while z < horizon and guard < 20000:
        guard += 1
        if valid and rng.random() < ratio:
            name, diff, lo, hi, plen, obs = valid[rng.randrange(len(valid))]
            rows, plen2, end_mask = stretch_rows(group_rows(obs), plen, speed, times, stretch, mask)
            rows, plen2 = (rows, plen2) if not stretch else (rows, plen2)
            start = max(z, spawn_floor())
            if stretch:
                opening, _ = row_lead(mask, rows[0][1], times)
                start = max(start, last_obstacle_z + opening * speed)
                mask = end_mask
            forcing = next((o for o, r in rows if len(r) >= 2), None)
            if forcing is not None:
                start = max(start, last_obstacle_z + times['forcing'] * speed - forcing)
            for off, row in rows:
                events.append((start + off, row))
                last_obstacle_z = max(last_obstacle_z, start + off)
                if len(row) >= 2:
                    last_forcing_z = max(last_forcing_z, start + off)
                if any(t == "ObstaclePalisade" for t, _, _ in row):
                    last_palisade_z = max(last_palisade_z, start + off)
            tail = rows[-1][0]
            z = max(start + plen2, start + tail + times['tail'] * speed)
            continue

        # random single / row
        start = max(z, spawn_floor())
        if rng.random() < 0.4:
            two = rng.random() < 0.5
            start = max(start, last_obstacle_z + times['forcing'] * speed)
            lanes = [-1, 0, 1]
            rng.shuffle(lanes)
            picked = lanes[:2] if two else lanes
            row = [(rand_type(rng), l, start) for l in picked]
            if not two and not any(t in PASSABLE for t, _, _ in row):
                row[rng.randrange(3)] = ("ObstacleJump", row[rng.randrange(3)][1], start)
            if floor:
                lead, mask = row_lead(mask, row, times)
                start = max(start, last_obstacle_z + lead * speed)
                row = [(t, l, start) for t, l, _ in row]
            events.append((start, row))
            last_obstacle_z = max(last_obstacle_z, start)
            last_forcing_z = max(last_forcing_z, start)
            if any(t == "ObstaclePalisade" for t, _, _ in row):
                last_palisade_z = max(last_palisade_z, start)
            z = start + gap()
        else:
            t = rand_type(rng)
            lane = rng.randrange(-1, 2)
            row = [(t, lane, start)]
            if floor:
                lead, mask = row_lead(mask, row, times)
                start = max(start, last_obstacle_z + lead * speed)
                row = [(t, lane, start)]
            events.append((start, row))
            last_obstacle_z = max(last_obstacle_z, start)
            if t == "ObstaclePalisade":
                last_palisade_z = max(last_palisade_z, start)
            z = start + gap()

    events.sort(key=lambda e: e[0])
    return events, speed


def rand_type(rng):
    r = rng.random()
    if r < 0.2:  return "ObstacleAvoid"
    if r < 0.4:  return "ObstacleJump"
    if r < 0.6:  return "ObstaclePalisade"
    if r < 0.8:  return "ObstaclePylon"
    return "ObstacleBroadJump"


def audit(levels, times, stretch, floor, runs=200):
    print(f"{'Lv':>3} {'min gap':>9} {'p5':>7} {'median':>8} {'obst/min':>9} {'unreachable':>13} {'worst deficit':>14}")
    print("-" * 70)
    totals = 0
    for level, cfg in levels.items():
        gaps, violations, n_events, n_min, worst = [], 0, 0, 1e9, 0.0
        for seed in range(runs):
            events, speed = simulate(level, cfg, times, stretch, floor, seed * 977 + level)
            n_events += len(events)
            mask = 0b111
            for i, (z, row) in enumerate(events):
                need, mask = row_lead(mask, row, times)
                if i == 0:
                    continue
                dt = (z - events[i - 1][0]) / speed
                gaps.append(dt)
                n_min = min(n_min, dt)
                if dt < need - 1e-4:
                    violations += 1
                    worst = max(worst, need - dt)
        duration = cfg[6]
        per_min = n_events / runs / duration * 60
        pct = 100.0 * violations / max(1, len(gaps))
        totals += violations
        print(f"{level:>3} {n_min:>8.2f}s {sorted(gaps)[len(gaps)//20]:>6.2f}s "
              f"{statistics.median(gaps):>7.2f}s {per_min:>9.0f} {pct:>12.1f}% {worst:>13.2f}s")
    print(f"\ntotal rows the player cannot reach in time: {totals}")


if __name__ == "__main__":
    print("=== BEFORE (fixed-unit spacing, 400ms model, no stretch) "
          "— measured against the 600ms budget ===")
    audit(OLD_LEVELS, dict(same=SAME_LANE, lane=LANE_CHANGE, forcing=FORCING_ROW,
                           tail=OLD_TIMES['tail'], pal=OLD_TIMES['pal']),
          stretch=False, floor=False)
    print()
    print("=== AFTER (time-based spacing, 600ms model, patterns stretched) ===")
    audit(NEW_LEVELS, dict(same=SAME_LANE, lane=LANE_CHANGE, forcing=FORCING_ROW,
                           tail=PATTERN_TAIL, pal=PALISADE_REC),
          stretch=True, floor=True)

"""Rewards: Combat.TallyRewards (0x8005E2AC) with Combat.AddChestLoot (0x8005E0F0), RollEquipmentDrops (0x8005DC78), RollChestItem
(0x8005DF38) and AddBonusLoot (0x8005E238), on the Combat checkpoints and the parity logs' won battles at their last action, with the enemy
list, the bonus loot and the members' experience changed one at a time, and the draws each left to the seed, all made to come up their
least (every chance landing, every count and gold at its least) or their most. Each vector gives the gold pool (doubled), the experience
pool, the loot as its stacks in order (the item's ID and count), each party member's total and spendable experience and Level after, and
the main stream's draws."""
import state
from draws import d100

FAMILY = 'Rewards'

TALLY_REWARDS = 0x8005E2AC
PARTY = 0x800D9684                # GameState.Party: the party, its four members' sheets first
ENEMIES = 0x800D9458              # BattleSetup.Enemies: 12 entity IDs
BONUS = 0x800D9470                # BattleSetup.BonusLoot

SEED = 0x1234
LEAST, MOST = d100(1), 0xFFFFFFFF


def member(st, i):
    return st.cpu.rd32(st.cpu.rd32(PARTY) + 4 * i)


def _enemies(st, a):
    ids = [state._int(x) for x in a]
    for i in range(12):
        st.cpu.wr16(ENEMIES + 2 * i, ids[i] if i < len(ids) else 0)


def _bonus(st, a):
    st.cpu.wr16(BONUS, state._int(a[0]))


def _exp(st, a):
    # exp:member,total,spendable: the sheet's experience (+0x14: total +4, spendable +8).
    e = st.cpu.rd32(member(st, state._int(a[0])) + 0x14)
    st.cpu.wr32(e + 4, state._int(a[1]))
    st.cpu.wr32(e + 8, state._int(a[2]))


EXTRA = {'enemies': _enemies, 'bonus': _bonus, 'exp': _exp}


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=EXTRA, seed=v['seed'], script=v.get('script'))
    c = st.combat
    st.call(TALLY_REWARDS, [c])
    loot = st.cpu.rd32(c + 0x4FB4)
    stacks = []
    for i in range(64):
        s = loot + 4 + 0x18 * i
        if st.cpu.rd16(s):
            stacks.append(f'0x{st.cpu.rd16(s):04X}*{st.cpu.rd32(s + 0x14)}')
    members = []
    for i in range(4):
        m = member(st, i)
        if m:
            e = st.cpu.rd32(m + 0x14)
            members.append(f'{st.cpu.rd32(e + 4)}/{st.cpu.rd32(e + 8)}/{st.cpu.rd8(st.cpu.rd32(m + 0x18) + 0xE + 6)}')
    expect = [('gold', st.cpu.rd32(c + 0x20)), ('experience', st.cpu.rd32(c + 0x24)), ('loot', ','.join(stacks) or '-'),
              ('members', ','.join(members)), ('draws', len(st.draws.drawn))]
    return expect, st.draws.callers()


# ---- the vectors

# Enemies by their chests (the entity's +0x7E): gold between bounds, the five equipment chances, a reagent, and drops of every kind of
# item: weapons, armor, potions, gems, scrolls, wands, rings.
ENTITIES = {
    'rat': 0x26E,               # chest 30: no gold, armor and shield 10, reagent 25
    'goblin': 0x206,            # chest 2: gold 10-50, all five chances, a potion and the 0x0A, 0x0F kinds
    'woodsman': 0x25A,          # chest 2, with armor and a weapon of each hand
    'chest9': 0x225,            # 1000-5000, four drops, counts between bounds
    'chest11': 0x213,           # 5000, every chance 100, six drops of six kinds
    'chest12': 0x21D,           # armor, shield and weapon at 100, drops between counts
    'chest13': 0x21F,
    'lugash': 0x262,            # chest 14
    'chest15': 0x223,           # every equipment chance 100: two weapons, no shield
    'chest18': 0x2C6,           # 20/10/20/10/20, a reagent and three drops
    'chest27': 0x244,
    'chest35': 0x20D,
    'chest38': 0x219,
    'chest39': 0x220,
    'chest43': 0x264,           # the reagent at 50, 1 to 10
    'gorgon': 0x217,            # chest 36: 100-200, a reagent
}

LOGS = {
    # Each won battle at its killing blow's dump, with the stream as TallyRewards found it.
    'setup3': ('20261031-172849-setup3', '0127.rdram.gz', 0x954927CD),
    'setup5': ('20261031-194638-setup5', '0113.rdram.gz', 0x91DFCDB6),
    'setup5b': ('20261031-201521-setup5b', '0085.rdram.gz', 0x63538851),
    'setup7': ('20261031-223128-setup7', '0047.rdram.gz', 0x8EBA0A63),
    'setup11': ('20261101-083418-setup11', '0293.rdram.gz', 0xF9B06DB9),
    'setup12b': ('20261101-100932-setup12b', '0015.rdram.gz', 0x19F3AB3C),
}


def cases():
    out = []

    def add(tag, pokes='', source='cp11', seed=SEED, each=None):
        v = dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=[], seed=seed, script={})
        if each is not None:
            # Every draw coming up `each`: a dry run with it finds how many there are.
            for _ in range(3):
                v['script'] = {i: each for i in range(len(run(v)[1]) + 1)}
            v['script'] = {i: each for i in range(len(run(v)[1]))}
        out.append(v)

    def three(tag, pokes='', source='cp11', seed=SEED):
        add(tag, pokes, source, seed)
        add(f'{tag}-least', pokes, source, seed, each=LEAST)
        add(f'{tag}-most', pokes, source, seed, each=MOST)

    # The rats' battle (checkpoint 11's list, ten rats), at three seeds and both ends.
    three('rats')
    add('rats-seed2', seed=0xDEADBEEF)

    # Each chest alone, and four of a kind (the gold, the pools and the stacks adding up).
    for name, e in ENTITIES.items():
        three(name, f'enemies:0x{e:X}')
    for name in ('goblin', 'chest9', 'chest18', 'chest43'):
        three(f'{name}x4', 'enemies:' + ','.join([f'0x{ENTITIES[name]:X}'] * 4))
    # Twelve of mixed kinds: the loot past a few stacks, the experience pool large.
    mix = ','.join(f'0x{e:X}' for e in list(ENTITIES.values())[1:13])
    three('mixed12', f'enemies:{mix}')
    add('mixed12-seed2', f'enemies:{mix}', seed=0x80000001)

    # The bonus loot: a chest rolled with no entity (no equipment), and items of each kind, worth no experience.
    for bonus in (0x801, 0x80B, 0x812, 0x81D):
        three(f'bonus{bonus:X}', f'bonus:0x{bonus:X}')
    for bonus in (0x10E, 0x729, 0x507, 0x1004):
        add(f'bonus{bonus:X}', f'bonus:0x{bonus:X}')

    # The members: dead ones gaining nothing, and the experience before the award near and far from the levels' thresholds.
    add('dead1', 'mod:1,Endurance,0|mod:1,Stamina,0|mod:1,Level,0')
    add('dead-all-but-alaron', ''.join(f'|mod:{i},Endurance,0|mod:{i},Stamina,0|mod:{i},Level,0' for i in (1, 2, 3)).lstrip('|'))
    for total in (0, 999, 1000, 4000, 20000, 100000, 1000000, 10000000):
        add(f'exp{total}', f'exp:0,{total},{total}|enemies:0x{ENTITIES["lugash"]:X}')

    # The won battles of the parity logs, at the original's seed and both ends.
    for tag, (folder, dump, seed) in LOGS.items():
        three(tag, source=f'log:{folder}/battle-01:{dump}', seed=seed)
    # Setup 13's Gorgons, never won, with their monster party's bonus loot (0x010E).
    three('setup13', source='log:20261101-102351-setup13/battle-01:0002.rdram.gz')
    return out

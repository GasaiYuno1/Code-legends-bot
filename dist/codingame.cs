using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System;

// ===== src/LocmBot/Battle/Evaluator.cs =====
namespace Locm
{
public sealed class Evaluator
{
public const double WinScore = 1e6;
public double AttackW = 1.0;
public double DefenseW = 0.8;
public double GuardW = 0.8;
public double GuardDefW = 0.25;
public double WardW = 1.2;
public double WardAtkW = 0.3;
public double LethalW = 1.5;
public double DrainAtkW = 0.3;
public double BreakthroughAtkW = 0.15;
public double ChargeW = 0.2;
public double Fragile1W = 0.0;
public double Fragile2W = 0.0;
public double BlueHandW = 0.0;
public double HpW = 0.1;
public double LowHpW = 0.8;
public int LowHp = 10;
public double MidHpW = 0.0;
public int MidHp = 20;
public double HandCardW = 1.0;
public double HandRatingW = 0.0;
public double OppDrawW = 1.5;
public double MyDrawW = 1.2;
public double Creature(in Creature c)
{
double v = c.Attack * AttackW + c.Defense * DefenseW;
Abilities a = c.Abilities;
if ((a & Abilities.Guard) != 0) v += GuardW + c.Defense * GuardDefW;
if ((a & Abilities.Ward) != 0) v += WardW + c.Attack * WardAtkW;
if ((a & Abilities.Lethal) != 0) v += LethalW;
if ((a & Abilities.Drain) != 0) v += c.Attack * DrainAtkW;
if ((a & Abilities.Breakthrough) != 0) v += c.Attack * BreakthroughAtkW;
if ((a & Abilities.Charge) != 0) v += ChargeW;
if (c.Defense <= 1) v -= Fragile1W;
else if (c.Defense == 2) v -= Fragile2W;
return v;
}
public double Health(int hp)
{
if (hp <= 0) return -WinScore;
double v = hp * HpW;
if (hp < MidHp) v -= (MidHp - hp) * MidHpW;
if (hp < LowHp) v -= (LowHp - hp) * LowHpW;
return v;
}
public double Hand(PlayerState p)
{
double v = p.HandCount * HandCardW;
if (HandRatingW != 0 || BlueHandW != 0)
{
for (int i = 0; i < p.HandKnown; i++)
{
if (HandRatingW != 0) v += HandRatingW * CardRating.Rate(p.Hand[i]);
if (BlueHandW != 0 && p.Hand[i].Type == CardType.BlueItem && p.Hand[i].Defense < 0) v += BlueHandW;
}
}
return v;
}
public double Score(GameState s, int me)
{
if (s.IsOver) return s.Winner == me ? WinScore : -WinScore;
var p = s.Players[me];
var o = s.Players[1 - me];
double v = 0;
for (int i = 0; i < p.BoardCount; i++) v += Creature(in p.Board[i]);
for (int i = 0; i < o.BoardCount; i++) v -= Creature(in o.Board[i]);
v += Health(p.Health) - Health(o.Health);
v += Hand(p) - Hand(o);
v -= Math.Max(0, o.NextTurnDraw - 1) * OppDrawW;
v += Math.Max(0, p.NextTurnDraw - 1) * MyDrawW;
return v;
}
}
}

// ===== src/LocmBot/Battle/IBattleStrategy.cs =====
namespace Locm
{
public interface IBattleStrategy
{
string PlayTurn(TurnInput input, TurnClock clock);
void WarmUp(TurnClock clock);
}
public sealed class PassBattle : IBattleStrategy
{
public string PlayTurn(TurnInput input, TurnClock clock) => "PASS";
public void WarmUp(TurnClock clock) { }
}
}

// ===== src/LocmBot/Battle/NetEval.cs =====
namespace Locm
{
public static class NetFeatures
{
public const int PerSide = 22;
public const int Count = PerSide * 2 + 5;
public static void Extract(GameState s, int me, float[] f)
{
var p = s.Players[me];
var o = s.Players[1 - me];
int k = 0;
int pAtk = Side(p, f, ref k);
int oAtk = Side(o, f, ref k);
f[k++] = (pAtk - o.Health) / 30f;
f[k++] = (oAtk - p.Health) / 30f;
f[k++] = (p.Health - o.Health) / 30f;
f[k++] = (p.MaxMana - o.MaxMana) / 12f;
f[k++] = s.Current == me ? 1f : 0f;
}
private static int Side(PlayerState p, float[] f, ref int k)
{
int sumA = 0, sumD = 0, maxA = 0, maxD = 0, guards = 0, guardDef = 0, wards = 0, lethal = 0, drain = 0, bt = 0, charge = 0, def1 = 0, minAD = 0;
for (int i = 0; i < p.BoardCount; i++)
{
ref Creature c = ref p.Board[i];
sumA += c.Attack;
sumD += c.Defense;
if (c.Attack > maxA) maxA = c.Attack;
if (c.Defense > maxD) maxD = c.Defense;
if (c.Has(Abilities.Guard)) { guards++; guardDef += c.Defense; }
if (c.Has(Abilities.Ward)) wards++;
if (c.Has(Abilities.Lethal)) lethal++;
if (c.Has(Abilities.Drain)) drain += c.Attack;
if (c.Has(Abilities.Breakthrough)) bt += c.Attack;
if (c.Has(Abilities.Charge)) charge++;
if (c.Defense <= 1) def1++;
minAD += Math.Min(c.Attack, c.Defense);
}
f[k++] = p.Health / 30f;
f[k++] = Math.Max(0, 10 - p.Health) / 10f;
f[k++] = Math.Max(0, 20 - p.Health) / 20f;
f[k++] = p.BoardCount / 6f;
f[k++] = sumA / 20f;
f[k++] = sumD / 20f;
f[k++] = maxA / 12f;
f[k++] = maxD / 12f;
f[k++] = guards / 3f;
f[k++] = guardDef / 15f;
f[k++] = wards / 3f;
f[k++] = lethal / 3f;
f[k++] = drain / 10f;
f[k++] = bt / 10f;
f[k++] = charge / 3f;
f[k++] = def1 / 3f;
f[k++] = minAD / 15f;
f[k++] = p.HandCount / 8f;
f[k++] = Math.Max(0, p.NextTurnDraw - 1) / 3f;
f[k++] = p.DeckSize / 30f;
f[k++] = p.NextRune / 25f;
f[k++] = p.MaxMana / 12f;
return sumA;
}
}
public sealed class NetEval
{
public static bool Available => NetWeights.Hidden > 0;
private readonly int _h;
private readonly float[] _w1;
private readonly float[] _b1;
private readonly float[] _w2;
private readonly float _b2;
private readonly float[] _f = new float[NetFeatures.Count];
public NetEval()
{
_h = NetWeights.Hidden;
var v = NetWeights.Packed.Length == 0 ? new string[0] : NetWeights.Packed.Split(',');
int n = NetFeatures.Count;
_w1 = new float[_h * n];
_b1 = new float[_h];
_w2 = new float[_h];
int i = 0;
for (int j = 0; j < _w1.Length && i < v.Length; j++) _w1[j] = int.Parse(v[i++]) / 10000f;
for (int j = 0; j < _h && i < v.Length; j++) _b1[j] = int.Parse(v[i++]) / 10000f;
for (int j = 0; j < _h && i < v.Length; j++) _w2[j] = int.Parse(v[i++]) / 10000f;
_b2 = i < v.Length ? int.Parse(v[i]) / 10000f : 0f;
}
public double Logit(GameState s, int me)
{
NetFeatures.Extract(s, me, _f);
int n = NetFeatures.Count;
double outv = _b2;
for (int j = 0; j < _h; j++)
{
double z = _b1[j];
int off = j * n;
for (int i = 0; i < n; i++) z += _w1[off + i] * _f[i];
outv += _w2[j] * Math.Tanh(z);
}
return outv;
}
}
}

// ===== src/LocmBot/Battle/NetWeights.cs =====
namespace Locm
{
public static class NetWeights
{
public const int Hidden = 32;
public const string Packed = "-46,1705,1533,-1683,-1774,869,2882,-289,-429,-1176,-1588,1288,-1828,1810,959,-683,1939,-3601,-2013,-7289,-1031,-228,-380,-1495,-3705,318,2452,943,140,467,-311,-597,326,-1050,-968,645,-2086,1166,1200,-96,1700,6231,18,581,3713,959,-3876,253,1467,1462,4924,2668,-2604,-919,-581,-4045,-2167,-1144,-266,-3054,-3211,522,-799,1369,-2817,-1520,-4280,-1101,-3582,-329,-5231,1643,-6789,-3468,988,1051,-448,-5804,-1799,-1627,-1201,1860,1564,3687,-386,827,1367,761,4025,32,7341,1904,-3265,-2688,-2836,-3831,-17832,683,1438,-533,-1277,-933,-157,-849,-1863,-2166,721,-1193,394,2118,-389,-542,-2078,740,-1084,7789,1176,9139,1941,798,-524,1902,3355,-5532,-1318,-1205,780,-1700,-70,211,53,-1474,-1311,280,1771,1500,-1833,-2934,-2174,-6002,-1342,-2810,-2844,-1706,297,5944,2027,-1734,1473,1614,-347,-362,-43,-1730,831,737,1462,-713,-566,1933,208,1119,664,-942,-5006,165,-7395,359,1684,-1306,-2394,624,-698,126,2749,-1031,1220,-165,-360,1933,806,-919,591,-2804,545,1342,889,1850,6917,1212,-1376,1439,2087,533,1324,-86,1546,-1643,-3255,872,1206,2116,638,-1826,-1813,854,871,845,32,-393,-1117,3001,2487,1927,781,3900,-119,-488,-3280,889,1249,753,-1892,167,-1788,-1014,2485,-3305,1104,-2753,-1867,-2544,-764,-19,-487,-2048,-1760,-3986,-673,2235,-457,-85,-744,-1243,-556,1573,-2801,-366,1936,344,576,-676,-1486,1727,776,-1332,-961,875,1190,-838,77,189,7178,-2110,2637,-992,390,3667,1160,-147,-2293,-4243,561,-1018,-3745,-2653,845,881,-1355,274,1943,-3104,-2875,-491,-1339,-4708,-4681,1776,-1314,-1343,-3205,-162,2321,1702,-1558,-578,1052,433,1622,4682,4315,3035,-1765,2727,-1170,562,-1650,2052,1085,3024,3207,-13135,-1877,-6013,-3528,738,-1286,-2095,-1396,1866,1268,1228,594,435,1160,-1913,-1603,-211,225,-674,470,2895,119,2758,1679,4116,-2385,1764,296,811,-3163,-4056,-486,1209,441,-1134,-1715,-2494,-448,-1598,-402,-3532,863,-2488,-2090,-287,-380,-393,-807,-2508,-5202,2835,-692,-2008,-1249,3127,-823,-3180,-602,1033,-2203,459,-1575,-1709,-1296,-101,2023,-991,-423,-3321,502,1622,5954,-80,7407,550,-2034,-1348,-1764,-2370,-6949,3408,-1108,-5913,-7799,4348,2576,1979,-611,-2438,-885,3170,1892,306,-3919,-145,1579,-808,3920,-169,3357,4328,428,-5442,456,2797,5851,-3728,-6151,-4517,919,-1497,3848,1352,2799,180,428,1532,-290,-1610,-1005,8494,1767,-1961,-1581,-5985,2590,1566,-1550,-1232,-2597,-673,78,1996,-1196,-2109,-615,3577,-2960,-1434,1679,-449,1503,381,-389,3627,-2148,307,-6265,1965,-4954,-691,1254,-725,387,-4143,4427,4669,-413,-1985,1478,375,1651,635,3806,1458,428,2793,858,473,3121,561,3486,-92,968,1258,4135,2289,941,64,546,182,-251,-2438,-1002,-823,-669,463,419,2337,739,-1170,-886,1441,589,-1250,-3049,-1037,-2614,-1573,528,-2515,157,743,-2349,-1417,845,-56,-416,1515,-1543,-116,1163,372,-178,-1523,-2284,790,-851,7830,192,5847,-834,739,-917,-881,-1034,1156,919,-2,1222,1890,-4219,-4298,-1790,-115,-516,2950,816,-3882,1906,3692,-21,4022,-318,94,744,-3628,-4778,-3225,955,-140,-7053,-6145,229,1815,-1138,-4383,-4563,-460,941,1264,744,-3226,-1011,1241,232,-523,4073,-1646,-636,1849,-74,754,-1175,-1285,5558,-377,-967,419,192,408,-3664,-1518,394,-1354,3062,-1109,-652,-570,1331,1381,5053,2232,1609,-1219,51,-1488,-2447,356,-2357,-1030,-1488,626,3396,634,-116,1350,-2066,-3078,1357,-2605,-3325,454,1334,-1821,1055,3890,2939,4869,552,-2526,-450,1029,3809,4959,886,-1147,-1478,-2601,2313,3485,1378,167,1997,499,1329,-879,3550,155,913,1262,2251,110,4321,793,3912,-588,3751,-1095,908,3575,-1088,784,-795,2749,95,-1199,-129,-4130,-795,-490,656,1565,-3063,3583,-5850,986,-5894,-1843,3190,1240,1724,1340,5946,-1760,249,702,317,-5247,-1826,-4196,-1701,-2471,-1076,48,-2410,-3240,-2632,1796,-472,-1593,-1436,-5955,-3654,-2409,-371,-3176,783,-920,-1097,447,2279,-287,-2599,-1594,1214,1712,1398,1762,1572,922,-1353,700,1244,2758,-226,7724,2649,-2111,-2996,-4557,2204,-10915,112,1381,-1241,-1460,1673,1142,1874,1605,-1825,-916,-933,2010,-1731,-1361,-1593,-698,-216,2783,3754,-706,4513,888,-1188,1366,425,1729,-1833,-4396,1517,2545,-241,1232,-1602,-1962,-651,413,273,2041,1215,-2254,-3515,-3173,-3733,989,-3059,591,-1162,-70,-262,1087,-690,-237,454,-596,959,-482,-2377,-114,-7292,894,188,-483,-4520,163,844,-504,-1631,1,-736,4360,-681,577,1253,2256,1369,-3228,-3155,-3761,-874,518,2459,472,-2676,839,3195,2121,2917,660,-2025,79,-421,-1658,1721,-1892,224,2809,-4229,-3124,495,-1124,-3958,-5095,2585,-119,279,-4188,-344,-187,1561,2603,3786,-7546,-899,361,-3516,-2184,4378,4702,6519,1449,-5517,2618,9408,7855,-6327,-8461,-4791,-1947,-1354,1732,-1185,-5083,-4638,3987,-2960,-2268,-719,-4986,1466,659,892,2810,-5676,887,653,-5686,21780,876,-644,1109,-1963,-918,-1628,635,1113,663,159,1905,-4688,4009,-445,-435,1090,136,-2743,-286,-2803,-4430,722,-1360,24,-2889,-3561,-579,1000,855,-90,2250,2289,-384,-253,-614,664,-2015,-1886,-1772,-1678,3428,555,4704,1534,-296,-377,902,-1389,4755,2981,-49,1090,-886,-1125,-504,1403,261,-1457,800,57,-279,-3615,5085,-44,1763,299,-1307,-1684,1617,-1555,-4414,2437,892,-1305,520,1609,3571,560,1407,2465,-584,2168,-2513,1985,1003,2229,3522,-1234,1905,3066,1947,5434,-1510,-428,2686,1159,2270,53,-1415,184,-1400,564,269,1744,1054,-486,2605,2292,368,-466,-230,-2680,1065,-1662,-1798,256,1045,733,5259,-97,1633,-1306,-113,149,344,-352,416,-2336,290,9,-2618,1221,-3052,-1634,-2955,-3670,-1112,1030,-5122,323,-9969,-1028,-1024,1073,736,2547,-1242,597,-1140,716,1537,-1109,-2559,-617,-681,-1785,68,1478,-1036,441,800,101,866,1500,-10,-4773,2772,-6560,-5009,2416,-1899,435,3724,3895,4879,3781,740,1222,937,-1791,2767,-42,1205,-294,921,2212,153,4360,1829,1271,-314,1103,1419,616,-1140,-4988,2238,3976,2696,634,-5093,-2079,-2947,-251,-2303,-3113,-553,-2173,-5293,5160,-1022,-1341,-3337,-1762,-4379,189,-2990,239,-671,904,-604,-2539,2179,1786,2069,-1559,-1743,-128,-2533,1265,5136,6889,2489,6171,3650,1893,-2275,86,4362,1515,3347,-5165,-2968,-496,-9810,1460,1293,450,3493,254,-532,3496,-843,582,530,2338,-1020,-3346,444,-803,-1150,-1787,-988,-2149,-1946,6061,-1332,380,-633,-1220,-1682,1929,1278,1904,2234,-371,1272,-3350,194,773,1182,-442,1250,1488,-481,2655,402,-6008,-701,-579,-2436,1075,-3720,-7962,1076,-4939,-900,-2037,1805,-1849,1055,-2291,-3736,2111,-66,2188,-735,1004,-2457,-1100,-1997,624,-1343,5934,-11766,-6599,-2702,1296,-9723,-7146,6108,3525,-61,346,443,4399,-1081,4523,1262,-7994,222,-2474,5104,4,6373,2174,1579,-72,-19,-580,2071,-3384,-14364,2332,-2716,-121,-142,-2586,-2327,-2025,467,319,1965,-2431,1897,-688,-3529,-3259,-3834,2326,2151,-4899,2322,-5204,-631,-1841,784,-1062,41,282,3444,3110,-604,-10,1257,92,-561,-1986,-428,-316,-1164,-1859,1209,2123,244,6112,666,-148,-1440,1114,2666,2795,-350,-599,1611,2617,-3847,541,790,-1614,-1500,-1264,1434,1057,-2556,-2608,1728,-608,-3906,3289,-8675,16200,-1179,1708,-6459,2171,2068,4478,-1826,666,-3185,-2159,-2561,-19,-2759,-126,146,2569,-1965,-753,-1704,-3123,13761,5941,9208,2246,370,337,-3040,-2982,-10790,-1070,-590,-453,1015,2437,3920,1009,-174,337,-441,597,-220,1363,1886,2482,1522,870,2519,4576,527,2127,816,770,3,73,1466,711,-1934,-643,-1349,1005,-121,-1093,-963,-340,1135,975,3537,1082,-36,-5076,-560,-5009,-3983,-965,-1163,774,326,-2632,-932,-762,1369,7042,-134,-2927,-4507,-2001,-2169,3321,64,-2471,-2067,-3690,1989,-1947,-2679,-1208,19803,16497,6844,243,-3747,-1268,912,1104,1594,3729,2051,-2912,-921,-1080,5705,2741,5178,-3576,919,-425,-151,1427,-1150,3131,-2012,1705,-850,1966,2226,1739,-12040,3861,262,1129,-1203,-1815,-440,315,1872,-603,754,55,-910,2750,-1584,92,1759,441,876,-1137,-1107,-4018,-338,-1678,-700,1426,-2202,276,1704,-934,2865,3242,1840,-1478,0,-1308,-2233,-477,-3511,-1001,495,8194,-1342,6424,-155,-3061,-3969,654,1809,8446,1257,4107,1988,5136,-6866,-3915,-3225,2295,666,2882,706,-3558,-4020,-5189,1624,-2591,-1953,-2193,-3230,1389,1781,-2799,-7514,-2098,330,579,673,1834,3205,-442,-783,-204,997,334,2476,-2441,-613,630,-1323,424,5511,822,7786,2402,-2665,-1092,18,9570,-8095,-44,-1068,-1342,-2192,2114,1731,2579,-439,-861,-3040,1116,-759,-107,-1071,247,-2171,-561,2275,-990,-1096,4947,1995,-2126,1169,4120,2214,-1788,-329,431,2832,1469,999,-1118,-1871,-1030,-1731,-14,-4589,1145,10,-2160,1023,-4023,1659,915,-422,1278,3284,-4828,-511,621,1613,-260,749,-677,-251,682,1580,-1458,-275,1345,-256,665,-1672,1556,-140,-514,-950,1047,479,-1226,402,2230,-360,294,417,883,-422,-30,928,1681,-157,-6618,-6450,8223,-10026,2853,4890,-6423,-6305,7424,-9280,-6290,-3911,-4935,5656,-6166,6103,2521,8576,-4312,-3533,8004,-7917,-4470,6924,-6555,-9318,-11064,7902,8281,-7026,-8722,5384,-514";
}
}

// ===== src/LocmBot/Battle/SearchBattle.cs =====
namespace Locm
{
public sealed class SearchBattle : IBattleStrategy
{
public const int MaxDepth = 20;
private struct Child
{
public GameAction Action;
public double Score;
}
private sealed class Candidate
{
public readonly GameState State = new GameState();
public readonly GameAction[] Line = new GameAction[MaxDepth + 1];
public int Length;
public double Static;
}
public readonly Evaluator Eval;
public Evaluator OppEval;
public bool UseNet = false;
public double NetScale = 10.0;
public bool NetAdditive = false;
private readonly NetEval _net = new NetEval();
public double ReplyWeight = 0.75;
public int MaxCandidates = 1024;
public double Phase1Share = 0.6;
public int DeepReplyCandidates = 32;
public int DeepReplyNodes = 400;
public int CounterCandidates = 0;
public int CounterNodes = 1500;
private readonly GameState[] _pool = new GameState[MaxDepth + 2];
private readonly List<GameAction>[] _legal = new List<GameAction>[MaxDepth + 1];
private readonly Child[][] _children = new Child[MaxDepth + 1][];
private readonly HashSet<ulong> _visited = new HashSet<ulong>();
private readonly GameAction[] _line = new GameAction[MaxDepth + 1];
private readonly GameAction[] _bestLine = new GameAction[MaxDepth + 1];
private readonly List<GameAction> _answer = new List<GameAction>();
private Candidate[] _heap;
private int _heapCount;
private readonly GameState _scratch = new GameState();
private readonly GameState _tmp = new GameState();
private readonly GameState _replyBest = new GameState();
private readonly GameState[] _cPool = new GameState[MaxDepth + 2];
private readonly List<GameAction>[] _cLegal = new List<GameAction>[MaxDepth + 1];
private readonly HashSet<ulong> _cVisited = new HashSet<ulong>();
private int _cNodes;
private int _cRootBoard;
private double _cBest;
private readonly GameState[] _oppPool = new GameState[GameState.MaxBoard + 2];
private readonly List<GameAction>[] _oppLegal = new List<GameAction>[GameState.MaxBoard + 2];
private readonly HashSet<ulong> _oppVisited = new HashSet<ulong>();
private readonly double[] _final = new double[4096];
private int _oppNodes;
private readonly GameAction[] _oppLine = new GameAction[GameState.MaxBoard + 2];
private readonly GameAction[] _oppBestLine = new GameAction[GameState.MaxBoard + 2];
private int _oppBestLen;
private double _oppBest;
private double _oppBestMine;
private int _oppMe;
private readonly int[] _order = new int[GameState.MaxBoard];
private readonly int[] _ids = new int[GameState.MaxBoard];
private int _bestLen;
private double _best;
private int _rootBoard;
private TurnClock _clock;
private long _phase1Deadline;
private bool _stop;
private bool _won;
private long _nodes;
private int _battleTurn = -1;
private bool _second;
private bool _sideKnown;
public long Nodes => _nodes;
public int Candidates { get; private set; }
public int Rescored { get; private set; }
public int DeepRescored { get; private set; }
public int CounterScored { get; private set; }
private readonly int[] _order2 = new int[4096];
public double BestScore => _best;
public bool TimedOut { get; private set; }
public SearchBattle() : this(new Evaluator()) { }
public SearchBattle(Evaluator eval)
{
Eval = eval;
OppEval = eval;
for (int i = 0; i < _pool.Length; i++) _pool[i] = new GameState();
for (int i = 0; i < _legal.Length; i++)
{
_legal[i] = new List<GameAction>(64);
_children[i] = new Child[128];
}
for (int i = 0; i < _oppPool.Length; i++)
{
_oppPool[i] = new GameState();
_oppLegal[i] = new List<GameAction>(64);
}
for (int i = 0; i < _cPool.Length; i++) _cPool[i] = new GameState();
for (int i = 0; i < _cLegal.Length; i++) _cLegal[i] = new List<GameAction>(64);
}
public string PlayTurn(TurnInput input, TurnClock clock)
{
_battleTurn++;
if (!_sideKnown)
{
_second = GameState.IsSecondPlayer(input);
_sideKnown = true;
}
_pool[0].Load(input, GameState.RefereeTurn(_battleTurn, _second));
return GameAction.Format(Search(_pool[0], clock));
}
public void WarmUp(TurnClock clock)
{
long budget = Math.Min(400, clock.RemainingMs - 400);
if (budget < 20) return;
var input = InputParser.ReadTurn(new System.IO.StringReader(WarmUpPosition));
_pool[0].Load(input, 10);
Search(_pool[0], new TurnClock((int)budget));
_battleTurn = -1;
_sideKnown = false;
}
public List<GameAction> Search(GameState root, TurnClock clock)
{
EnsureHeap();
_clock = clock;
_phase1Deadline = clock.ElapsedMs + (long)(clock.RemainingMs * Phase1Share);
_stop = false;
_won = false;
TimedOut = false;
_nodes = 0;
_heapCount = 0;
Rescored = 0;
DeepRescored = 0;
CounterScored = 0;
_visited.Clear();
if (!ReferenceEquals(root, _pool[0])) _pool[0].CopyFrom(root);
int me = _pool[0].Current;
_rootBoard = _pool[0].Me.BoardCount;
_best = Eval.Score(_pool[0], me);
_bestLen = 0;
_visited.Add(_pool[0].Hash());
if (!_pool[0].IsOver)
{
_line[0] = GameAction.Pass;
AddCandidate(_pool[0], _best, 0);
Dfs(0);
}
Candidates = _heapCount;
if (!_won && !_pool[0].IsOver) Rescore(me);
_answer.Clear();
for (int i = 0; i < _bestLen; i++) _answer.Add(_bestLine[i]);
return _answer;
}
private void Dfs(int depth)
{
if (depth >= MaxDepth) return;
var s = _pool[depth];
var child = _pool[depth + 1];
int me = s.Current;
var legal = _legal[depth];
s.LegalActions(legal);
var kids = _children[depth];
int n = 0;
ActionType last = depth == 0 ? ActionType.Pass : _line[depth - 1].Type;
for (int i = 0; i < legal.Count && n < kids.Length; i++)
{
GameAction a = legal[i];
if (a.IsPass || !AllowedAfter(last, a.Type, s)) continue;
child.CopyFrom(s);
child.Apply(a);
if (!_visited.Add(child.Hash())) continue;
double v = Eval.Score(child, me);
_line[depth] = a;
if (v > _best)
{
_best = v;
_bestLen = depth + 1;
Array.Copy(_line, _bestLine, _bestLen);
}
if (child.IsOver)
{
if (child.Winner == me)
{
_stop = true;
_won = true;
_best = v;
_bestLen = depth + 1;
Array.Copy(_line, _bestLine, _bestLen);
return;
}
continue;
}
AddCandidate(child, v, depth + 1);
kids[n].Action = a;
kids[n].Score = v;
n++;
}
if (_stop) return;
for (int i = 1; i < n; i++)
{
Child k = kids[i];
int j = i - 1;
while (j >= 0 && kids[j].Score < k.Score) { kids[j + 1] = kids[j]; j--; }
kids[j + 1] = k;
}
for (int i = 0; i < n; i++)
{
if ((++_nodes & 31) == 0 && _clock.ElapsedMs >= _phase1Deadline)
{
TimedOut = true;
_stop = true;
}
if (_stop) return;
child.CopyFrom(s);
child.Apply(kids[i].Action);
_line[depth] = kids[i].Action;
Dfs(depth + 1);
}
}
private bool AllowedAfter(ActionType last, ActionType next, GameState s)
{
switch (next)
{
case ActionType.Summon:
if (last == ActionType.Pass || last == ActionType.Summon) return true;
return s.Me.BoardCount < _rootBoard;
case ActionType.Use:
return last != ActionType.Attack;
default:
return true;
}
}
private void EnsureHeap()
{
if (_heap != null && _heap.Length == MaxCandidates) return;
_heap = new Candidate[MaxCandidates];
for (int i = 0; i < _heap.Length; i++) _heap[i] = new Candidate();
_heapCount = 0;
}
private void AddCandidate(GameState state, double score, int lineLen)
{
Candidate c;
if (_heapCount < _heap.Length)
{
c = _heap[_heapCount++];
Fill(c, state, score, lineLen);
SiftUp(_heapCount - 1);
}
else
{
if (score <= _heap[0].Static) return;
c = _heap[0];
Fill(c, state, score, lineLen);
SiftDown(0);
}
}
private void Fill(Candidate c, GameState state, double score, int lineLen)
{
c.State.CopyFrom(state);
c.Static = score;
c.Length = lineLen;
Array.Copy(_line, c.Line, lineLen);
}
private void SiftUp(int i)
{
while (i > 0)
{
int parent = (i - 1) >> 1;
if (_heap[parent].Static <= _heap[i].Static) break;
Swap(parent, i);
i = parent;
}
}
private void SiftDown(int i)
{
while (true)
{
int l = 2 * i + 1, r = l + 1, m = i;
if (l < _heapCount && _heap[l].Static < _heap[m].Static) m = l;
if (r < _heapCount && _heap[r].Static < _heap[m].Static) m = r;
if (m == i) return;
Swap(m, i);
i = m;
}
}
private void Swap(int a, int b)
{
Candidate t = _heap[a];
_heap[a] = _heap[b];
_heap[b] = t;
}
private void Rescore(int me)
{
int n = _heapCount;
Array.Sort(_heap, 0, n, StaticDesc.Instance);
double bestFinal = double.NegativeInfinity;
int bestIdx = -1;
int scored = 0;
for (int i = 0; i < n; i++)
{
if ((i & 7) == 7 && _clock.TimeUp)
{
TimedOut = true;
break;
}
Candidate c = _heap[i];
double reply = ReplyScore(c.State, me);
double stat = UseNet ? Leaf(c.State, me) : c.Static;
double final = ReplyWeight * reply + (1 - ReplyWeight) * stat;
if (i < _final.Length) _final[i] = final;
scored = i + 1;
Rescored++;
if (final > bestFinal)
{
bestFinal = final;
bestIdx = i;
}
}
if (bestIdx < 0) return;
if (DeepReplyCandidates > 0 && !_clock.TimeUp)
{
int m = Math.Min(scored, Math.Min(DeepReplyCandidates, _final.Length));
var order = _order2;
for (int i = 0; i < scored && i < order.Length; i++) order[i] = i;
int total = Math.Min(scored, order.Length);
for (int i = 0; i < m; i++)
{
int best = i;
for (int j = i + 1; j < total; j++) if (_final[order[j]] > _final[order[best]]) best = j;
int t = order[i]; order[i] = order[best]; order[best] = t;
}
bestFinal = double.NegativeInfinity;
bestIdx = -1;
int deepDone = 0;
for (int i = 0; i < m; i++)
{
if (_clock.TimeUp) { TimedOut = true; break; }
Candidate c = _heap[order[i]];
double deep = DeepReplyScore(c.State, me);
double final = ReplyWeight * deep + (1 - ReplyWeight) * (UseNet ? Leaf(c.State, me) : c.Static);
_final[order[i]] = final;
DeepRescored++;
deepDone = i + 1;
if (final > bestFinal)
{
bestFinal = final;
bestIdx = order[i];
}
}
if (bestIdx < 0) return;
if (CounterCandidates > 0 && !_clock.TimeUp && !_heap[bestIdx].State.IsOver)
{
int k = Math.Min(deepDone, CounterCandidates);
for (int i = 0; i < k; i++)
{
int best = i;
for (int j = i + 1; j < deepDone; j++) if (_final[order[j]] > _final[order[best]]) best = j;
int t = order[i]; order[i] = order[best]; order[best] = t;
}
double bestCounter = double.NegativeInfinity;
int bestCounterIdx = -1;
for (int i = 0; i < k; i++)
{
if (_clock.TimeUp) { TimedOut = true; break; }
Candidate c = _heap[order[i]];
DeepReplyScore(c.State, me);
double counter = CounterScore(me);
double final = ReplyWeight * counter + (1 - ReplyWeight) * (UseNet ? Leaf(c.State, me) : c.Static);
CounterScored++;
if (final > bestCounter)
{
bestCounter = final;
bestCounterIdx = order[i];
}
}
if (bestCounterIdx >= 0)
{
bestFinal = bestCounter;
bestIdx = bestCounterIdx;
}
}
}
Candidate b = _heap[bestIdx];
_best = bestFinal;
_bestLen = b.Length;
Array.Copy(b.Line, _bestLine, b.Length);
_heapCount = 0;
}
private sealed class StaticDesc : IComparer<Candidate>
{
public static readonly StaticDesc Instance = new StaticDesc();
public int Compare(Candidate a, Candidate b) => b.Static.CompareTo(a.Static);
}
public double ReplyScore(GameState after, int me)
{
if (after.IsOver) return Leaf(after, me);
var s = _scratch;
s.CopyFrom(after);
s.EndTurn();
if (s.IsOver) return Leaf(s, me);
int opp = s.Current;
var o = s.Players[opp];
var p = s.Players[me];
int totalAttack = 0;
for (int i = 0; i < o.BoardCount; i++) totalAttack += o.Board[i].Attack;
int guardDefense = 0;
for (int i = 0; i < p.BoardCount; i++)
if (p.Board[i].Has(Abilities.Guard)) guardDefense += p.Board[i].Defense;
if (totalAttack - guardDefense >= p.Health) return -Evaluator.WinScore;
int n = o.BoardCount;
for (int i = 0; i < n; i++) _order[i] = i;
for (int i = 1; i < n; i++)
{
int k = _order[i];
int j = i - 1;
while (j >= 0 && o.Board[_order[j]].Attack < o.Board[k].Attack) { _order[j + 1] = _order[j]; j--; }
_order[j + 1] = k;
}
for (int i = 0; i < n; i++) _ids[i] = o.Board[_order[i]].InstanceId;
for (int i = 0; i < n && !s.IsOver; i++)
{
int ai = o.FindCreature(_ids[i]);
if (ai < 0 || !o.Board[ai].CanAttack) continue;
int id = _ids[i];
double bestSc = OppEval.Score(s, opp);
int bestTarget = int.MinValue;
bool guards = p.HasGuard();
if (!guards)
{
double sc = TryAttack(s, id, GameAction.Face, opp);
if (sc > bestSc) { bestSc = sc; bestTarget = GameAction.Face; }
}
for (int t = 0; t < p.BoardCount; t++)
{
if (guards && !p.Board[t].Has(Abilities.Guard)) continue;
int tid = p.Board[t].InstanceId;
double sc = TryAttack(s, id, tid, opp);
if (sc > bestSc) { bestSc = sc; bestTarget = tid; }
}
if (bestTarget != int.MinValue) s.Apply(GameAction.Attack(id, bestTarget));
}
return Leaf(s, me);
}
public double DeepReplyScore(GameState after, int me)
{
if (after.IsOver) return Leaf(after, me);
var s = _oppPool[0];
s.CopyFrom(after);
s.EndTurn();
if (s.IsOver) return Leaf(s, me);
int opp = s.Current;
_oppVisited.Clear();
_oppVisited.Add(s.Hash());
_oppNodes = 0;
_oppMe = me;
_oppBest = OppEval.Score(s, opp);
_oppBestMine = Leaf(s, me);
_replyBest.CopyFrom(s);
_oppBestLen = 0;
OppDfs(0, opp);
return _oppBestMine;
}
public double Leaf(GameState s, int me)
{
if (s.IsOver || !UseNet || !NetEval.Available) return Eval.Score(s, me);
double net = NetScale * _net.Logit(s, me);
return NetAdditive ? Eval.Score(s, me) + net : net;
}
public void PredictReply(GameState after, int me, List<GameAction> into)
{
into.Clear();
DeepReplyScore(after, me);
for (int i = 0; i < _oppBestLen; i++) into.Add(_oppBestLine[i]);
}
public double CounterScore(int me)
{
var s = _cPool[0];
s.CopyFrom(_replyBest);
if (s.IsOver) return Eval.Score(s, me);
s.EndTurn();
if (s.IsOver) return Eval.Score(s, me);
if (s.Current != me) return Eval.Score(s, me);
_cVisited.Clear();
_cVisited.Add(s.Hash());
_cNodes = 0;
_cRootBoard = s.Me.BoardCount;
_cBest = Eval.Score(s, me);
CounterDfs(0, ActionType.Pass, me);
return _cBest;
}
private void CounterDfs(int depth, ActionType last, int me)
{
if (depth + 1 >= _cPool.Length) return;
var s = _cPool[depth];
var child = _cPool[depth + 1];
var legal = _cLegal[depth];
s.LegalActions(legal);
for (int i = 0; i < legal.Count; i++)
{
GameAction a = legal[i];
if (a.IsPass || !CounterAllowed(last, a.Type, s)) continue;
if (_cNodes >= CounterNodes) return;
child.CopyFrom(s);
child.Apply(a);
_cNodes++;
if (!_cVisited.Add(child.Hash())) continue;
double v = Eval.Score(child, me);
if (v > _cBest) _cBest = v;
if (child.IsOver)
{
if (child.Winner == me) { _cNodes = CounterNodes; return; }
continue;
}
CounterDfs(depth + 1, a.Type, me);
}
}
private bool CounterAllowed(ActionType last, ActionType next, GameState s)
{
switch (next)
{
case ActionType.Summon:
if (last == ActionType.Pass || last == ActionType.Summon) return true;
return s.Me.BoardCount < _cRootBoard;
case ActionType.Use:
return last != ActionType.Attack;
default:
return true;
}
}
private void OppDfs(int depth, int opp)
{
if (depth + 1 >= _oppPool.Length) return;
var s = _oppPool[depth];
var child = _oppPool[depth + 1];
var legal = _oppLegal[depth];
s.LegalActions(legal);
for (int i = 0; i < legal.Count; i++)
{
GameAction a = legal[i];
if (a.Type != ActionType.Attack) continue;
if (_oppNodes >= DeepReplyNodes) return;
child.CopyFrom(s);
child.Apply(a);
_oppNodes++;
if (!_oppVisited.Add(child.Hash())) continue;
_oppLine[depth] = a;
double v = OppEval.Score(child, opp);
if (v > _oppBest)
{
_oppBest = v;
_oppBestMine = Leaf(child, _oppMe);
_replyBest.CopyFrom(child);
_oppBestLen = depth + 1;
Array.Copy(_oppLine, _oppBestLine, _oppBestLen);
}
if (child.IsOver) continue;
OppDfs(depth + 1, opp);
}
}
private double TryAttack(GameState s, int id, int target, int opp)
{
_tmp.CopyFrom(s);
_tmp.Apply(GameAction.Attack(id, target));
return OppEval.Score(_tmp, opp);
}
private const string WarmUpPosition =
"22 8 15 20 1\n" +
"19 7 14 15 1\n" +
"4 0\n" +
"11\n" +
"18 12 0 0 4 7 4 ------ 0 0 0\n" +
"148 15 0 2 2 0 -2 BCDGLW 0 0 0\n" +
"124 17 0 1 3 2 1 --D--- 0 0 0\n" +
"158 19 0 3 3 0 -4 ------ 0 0 0\n" +
"53 21 0 0 4 1 1 -C--L- 0 0 0\n" +
"3 9 1 0 1 2 2 ------ 0 0 0\n" +
"104 11 1 0 4 4 1 --D--W 0 0 0\n" +
"110 13 1 0 5 0 9 ---G-- 0 0 0\n" +
"7 8 -1 0 2 2 2 -----W 0 0 0\n" +
"115 10 -1 0 8 5 5 ---G-W 0 0 0\n" +
"20 14 -1 0 5 8 2 ------ 0 0 0\n";
}
}

// ===== src/LocmBot/Bot.cs =====
namespace Locm
{
public sealed class Bot
{
public const int DraftTurns = 30;
private readonly IDraftStrategy _draft;
private readonly IBattleStrategy _battle;
private readonly TextWriter _log;
private readonly TextWriter _dump;
private readonly List<Card> _picked = new List<Card>();
private int _turn;
public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log) : this(draft, battle, log, null) { }
public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log, TextWriter dump)
{
_draft = draft;
_battle = battle;
_log = log;
_dump = dump;
}
public void Run(TextReader input, TextWriter output)
{
var recorder = _dump != null ? new RecordingReader(input) : null;
if (recorder != null) input = recorder;
while (true)
{
string first = input.ReadLine();
if (first == null) return;
var clock = new TurnClock((_turn == 0 || _turn == DraftTurns ? TimeLimits.FirstTurnMs : TimeLimits.TurnMs) - TimeLimits.SafetyMarginMs);
string answer;
try
{
TurnInput turn = InputParser.ReadTurn(first, input);
answer = PlayTurn(turn, clock);
}
catch (Exception e)
{
_log.WriteLine("Turn error: " + e.Message);
answer = "PASS";
}
output.WriteLine(answer);
output.Flush();
if (recorder != null)
{
_dump.Write(recorder.Take());
_dump.WriteLine("> " + answer);
_dump.Flush();
}
_log.WriteLine($"turn {_turn} done in {clock.ElapsedMs} ms: {answer}");
_turn++;
}
}
public string PlayTurn(TurnInput turn, TurnClock clock)
{
bool isDraft = _turn < DraftTurns && turn.LooksLikeDraft;
if (isDraft)
{
if (_turn == 0)
{
try { _battle.WarmUp(clock); }
catch (Exception e) { _log.WriteLine("WarmUp error: " + e.Message); }
}
int idx = _draft.Pick(turn, _picked);
if (idx < 0 || idx > 2) idx = 0;
_picked.Add(turn.Cards[idx]);
return "PICK " + idx;
}
string actions = _battle.PlayTurn(turn, clock);
return string.IsNullOrWhiteSpace(actions) ? "PASS" : actions;
}
private sealed class RecordingReader : TextReader
{
private readonly TextReader _inner;
private readonly StringBuilder _buf = new StringBuilder();
public RecordingReader(TextReader inner) { _inner = inner; }
public override string ReadLine()
{
string line = _inner.ReadLine();
if (line != null) _buf.Append(line).Append('\n');
return line;
}
public override int Read() => _inner.Read();
public override int Peek() => _inner.Peek();
public string Take()
{
string s = _buf.ToString();
_buf.Clear();
return s;
}
}
}
}

// ===== src/LocmBot/Draft/CardRating.cs =====
namespace Locm
{
public static class CardRating
{
public static bool UseTable = true;
public static double AttackW = 1.0;
public static double DefenseW = 1.0;
public static double BodyW = 0.06;
public static double GuardW = 1.0;
public static double GuardDefW = 0.2;
public static double WardW = 1.5;
public static double WardAtkW = 0.3;
public static double LethalW = 4.0;
public static double LethalAtkW = -0.3;
public static double ChargeLethalW = 3.5;
public static double DrainAtkW = 0.3;
public static double BreakthroughAtkW = 0.2;
public static double ChargeW = 0.8;
public static double ChargeAtkW = 0.2;
public static double DrawW = 2.0;
public static double OppDamageW = 0.4;
public static double MyHealW = 0.25;
public static double ItemDamageW = 1.4;
public static int ItemDamageCap = 8;
public static double ItemRemoveAllW = 2.0;
public static double ItemRemoveGuardW = 0.5;
public static double BlueFlexW = 1.0;
public static double Par(int cost) => 2.0 * cost + 2.0;
public static double Abilities(Abilities a, int attack, int defense)
{
double v = 0;
if ((a & Locm.Abilities.Guard) != 0) v += GuardW + defense * GuardDefW;
if ((a & Locm.Abilities.Ward) != 0) v += WardW + attack * WardAtkW;
if ((a & Locm.Abilities.Lethal) != 0) v += LethalW + attack * LethalAtkW;
if ((a & Locm.Abilities.Drain) != 0) v += attack * DrainAtkW;
if ((a & Locm.Abilities.Breakthrough) != 0) v += attack * BreakthroughAtkW;
if ((a & Locm.Abilities.Charge) != 0) v += ChargeW + attack * ChargeAtkW;
if ((a & (Locm.Abilities.Charge | Locm.Abilities.Lethal)) == (Locm.Abilities.Charge | Locm.Abilities.Lethal)) v += ChargeLethalW;
return v;
}
public static double Effects(Card c)
{
double v = c.CardDraw * DrawW;
if (c.OpponentHealthChange < 0) v += -c.OpponentHealthChange * OppDamageW;
else v -= c.OpponentHealthChange * OppDamageW;
v += c.MyHealthChange * MyHealW;
return v;
}
public static double Rate(Card c)
{
if (UseTable && CardTable.Picks > 0 && c.Number > 0 && c.Number < CardTable.Rating.Length)
return CardTable.Rating[c.Number];
return Formula(c);
}
public static double Formula(Card c)
{
switch (c.Type)
{
case CardType.Creature:
return c.Attack * AttackW + c.Defense * DefenseW + c.Attack * c.Defense * BodyW
+ Abilities(c.Abilities, c.Attack, c.Defense) + Effects(c) - Par(c.Cost);
case CardType.GreenItem:
{
double v = c.Attack * AttackW + c.Defense * DefenseW + Abilities(c.Abilities, 3, 3) + Effects(c);
return v - (1.5 * c.Cost + 1.5);
}
case CardType.RedItem:
case CardType.BlueItem:
{
double dmg = Math.Min(-c.Defense, ItemDamageCap);
double v = dmg * ItemDamageW + (-c.Attack) * AttackW * 0.8 + Effects(c);
if (c.Abilities == (Locm.Abilities.Breakthrough | Locm.Abilities.Charge | Locm.Abilities.Drain | Locm.Abilities.Guard | Locm.Abilities.Lethal | Locm.Abilities.Ward))
v += ItemRemoveAllW;
else if ((c.Abilities & Locm.Abilities.Guard) != 0) v += ItemRemoveGuardW;
if (c.Type == CardType.BlueItem && c.Defense < 0) v += BlueFlexW;
return v - (2.0 * c.Cost + 1.0);
}
}
return 0;
}
}
}

// ===== src/LocmBot/Draft/CardTable.cs =====
namespace Locm
{
public static class CardTable
{
public const int Games = 1474;
public const int Picks = 71010;
private const string Packed = "-39,-207,161,-111,57,47,308,96,161,-271,123,72,19,-161,90,-118,118,269,166,-223,118,-79,216,-259,-66,62,-106,201,264,-28,-280,258,227,16,-216,35,242,-55,-34,-158,27,-252,-168,268,-111,-183,-66,290,296,239,299,242,293,220,-366,-118,-290,-122,13,-242,41,-41,-252,210,313,237,276,320,293,100,-102,-27,27,-74,129,-152,0,-285,-7,272,111,219,16,244,197,-18,179,169,-71,-20,-59,-375,3,-64,173,150,70,-15,188,-61,-121,-228,224,66,129,118,-252,-199,166,-461,133,27,-340,136,160,288,-296,-60,-89,-74,150,-25,-183,-291,-152,-12,-119,91,97,-206,-243,-208,162,33,75,-181,-74,-350,277,-341,87,-215,-418,120,63,-143,192,177,-87,151,298,159,-524,-488,35,-405,76,183,-114,-475";
private const string PackedWin = "-63,-213,171,-128,56,11,319,89,162,-262,145,62,18,-148,78,-120,113,248,168,-217,113,-83,207,-256,-92,47,-81,177,275,-12,-295,272,220,-20,-213,54,240,-72,-30,-175,24,-249,-188,269,-94,-172,-50,290,289,224,304,223,316,247,-363,-128,-287,-114,37,-254,63,-13,-255,215,321,243,283,320,281,98,-126,-20,5,-76,133,-176,-42,-273,1,240,106,231,21,214,206,2,177,141,-93,-3,-74,-379,7,-68,211,146,74,-17,197,-73,-121,-228,221,68,126,153,-264,-229,183,-461,141,20,-337,127,177,290,-293,-19,-67,-89,173,-17,-178,-322,-194,-5,-136,119,109,-212,-234,-228,175,20,92,-162,-93,-357,304,-341,89,-226,-425,158,44,-166,197,131,-144,134,308,174,-524,-488,31,-402,44,184,-119,-475";
public static bool UseWinAdjusted = false;
public static double[] Rating => UseWinAdjusted ? _win : _pick;
private static readonly double[] _pick = Unpack(Packed);
private static readonly double[] _win = Unpack(PackedWin);
private static double[] Unpack(string packed)
{
var parts = packed.Split(',');
var r = new double[parts.Length + 1];
for (int i = 0; i < parts.Length; i++) r[i + 1] = int.Parse(parts[i]) / 100.0;
return r;
}
}
}

// ===== src/LocmBot/Draft/IDraftStrategy.cs =====
namespace Locm
{
public interface IDraftStrategy
{
int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked);
}
public sealed class FirstCardDraft : IDraftStrategy
{
public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked) => 0;
}
public sealed class RatingDraft : IDraftStrategy
{
public static double[] TargetCurve = { 0.6, 1.6, 6.4, 5.3, 5.8, 3.5, 3.0, 3.6 };
public static double CurveW = 0.1;
public static int MaxItems = 8;
public static double ItemOverPenalty = 3.0;
public static int MaxSameCard = 2;
public static double SameCardPenalty = 0.0;
private readonly int[] _curve = new int[8];
public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked)
{
Array.Clear(_curve, 0, _curve.Length);
int items = 0;
foreach (var c in alreadyPicked)
{
_curve[Bucket(c.Cost)]++;
if (c.IsItem) items++;
}
int best = 0;
double bestScore = double.NegativeInfinity;
for (int i = 0; i < input.Cards.Count && i < 3; i++)
{
double s = Score(input.Cards[i], alreadyPicked, items);
if (s > bestScore)
{
bestScore = s;
best = i;
}
}
return best;
}
public double Score(Card c, IReadOnlyList<Card> alreadyPicked, int items)
{
double s = CardRating.Rate(c);
int b = Bucket(c.Cost);
double expected = TargetCurve[b] * alreadyPicked.Count / 30.0;
s += (expected - _curve[b]) * CurveW;
if (c.IsItem && items >= MaxItems) s -= ItemOverPenalty;
int copies = 0;
foreach (var p in alreadyPicked) if (p.Number == c.Number) copies++;
if (copies >= MaxSameCard) s -= SameCardPenalty;
return s;
}
private static int Bucket(int cost) => cost < 7 ? cost : 7;
}
}

// ===== src/LocmBot/Game/Abilities.cs =====
namespace Locm
{
[Flags]
public enum Abilities : byte
{
None = 0,
Breakthrough = 1 << 0,
Charge = 1 << 1,
Drain = 1 << 2,
Guard = 1 << 3,
Lethal = 1 << 4,
Ward = 1 << 5,
}
public static class AbilitiesExt
{
public static Abilities Parse(string s)
{
Abilities a = Abilities.None;
foreach (char c in s)
{
switch (c)
{
case 'B': a |= Abilities.Breakthrough; break;
case 'C': a |= Abilities.Charge; break;
case 'D': a |= Abilities.Drain; break;
case 'G': a |= Abilities.Guard; break;
case 'L': a |= Abilities.Lethal; break;
case 'W': a |= Abilities.Ward; break;
}
}
return a;
}
public static string Format(this Abilities a)
{
var sb = new StringBuilder(6);
sb.Append((a & Abilities.Breakthrough) != 0 ? 'B' : '-');
sb.Append((a & Abilities.Charge) != 0 ? 'C' : '-');
sb.Append((a & Abilities.Drain) != 0 ? 'D' : '-');
sb.Append((a & Abilities.Guard) != 0 ? 'G' : '-');
sb.Append((a & Abilities.Lethal) != 0 ? 'L' : '-');
sb.Append((a & Abilities.Ward) != 0 ? 'W' : '-');
return sb.ToString();
}
public static bool Has(this Abilities a, Abilities flag) => (a & flag) != 0;
}
}

// ===== src/LocmBot/Game/Card.cs =====
namespace Locm
{
public enum CardType : byte
{
Creature = 0,
GreenItem = 1,
RedItem = 2,
BlueItem = 3,
}
public enum Location : sbyte
{
OpponentBoard = -1,
MyHand = 0,
MyBoard = 1,
}
public readonly struct Card
{
public readonly int Number;
public readonly int InstanceId;
public readonly Location Location;
public readonly CardType Type;
public readonly int Cost;
public readonly int Attack;
public readonly int Defense;
public readonly Abilities Abilities;
public readonly int MyHealthChange;
public readonly int OpponentHealthChange;
public readonly int CardDraw;
public Card(int number, int instanceId, Location location, CardType type, int cost, int attack, int defense,
Abilities abilities, int myHealthChange, int opponentHealthChange, int cardDraw)
{
Number = number;
InstanceId = instanceId;
Location = location;
Type = type;
Cost = cost;
Attack = attack;
Defense = defense;
Abilities = abilities;
MyHealthChange = myHealthChange;
OpponentHealthChange = opponentHealthChange;
CardDraw = cardDraw;
}
public bool IsCreature => Type == CardType.Creature;
public bool IsItem => Type != CardType.Creature;
public Card WithInstance(int instanceId, Location location) =>
new Card(Number, instanceId, location, Type, Cost, Attack, Defense, Abilities, MyHealthChange, OpponentHealthChange, CardDraw);
public string ToInputLine() => ToInputLine(Location);
public string ToInputLine(Location location) =>
$"{Number} {InstanceId} {(int)location} {(int)Type} {Cost} {Attack} {Defense} {Abilities.Format()} {MyHealthChange} {OpponentHealthChange} {CardDraw}";
public override string ToString() =>
$"#{Number}/{InstanceId} {Type} {Cost}m {Attack}/{Defense} {Abilities.Format()} hp{MyHealthChange:+0;-0;0}/{OpponentHealthChange:+0;-0;0} draw{CardDraw}";
}
}

// ===== src/LocmBot/Game/CardDb.cs =====
namespace Locm
{
public static class CardDb
{
public const int Count = 160;
private static readonly Card[] _cards = Build();
private static readonly string[] _names = BuildNames();
public static bool Contains(int baseId) => baseId >= 1 && baseId <= Count;
public static Card Get(int baseId)
{
if (!Contains(baseId)) throw new System.ArgumentOutOfRangeException(nameof(baseId), "unknown card " + baseId);
return _cards[baseId - 1];
}
public static string Name(int baseId) => Contains(baseId) ? _names[baseId - 1] : "?";
private static Card[] Build()
{
var c = new Card[Count];
c[0] = new Card(1, -1, Location.MyHand, CardType.Creature, 1, 2, 1, Abilities.None, 1, 0, 0);
c[1] = new Card(2, -1, Location.MyHand, CardType.Creature, 1, 1, 2, Abilities.None, 0, -1, 0);
c[2] = new Card(3, -1, Location.MyHand, CardType.Creature, 1, 2, 2, Abilities.None, 0, 0, 0);
c[3] = new Card(4, -1, Location.MyHand, CardType.Creature, 2, 1, 5, Abilities.None, 0, 0, 0);
c[4] = new Card(5, -1, Location.MyHand, CardType.Creature, 2, 4, 1, Abilities.None, 0, 0, 0);
c[5] = new Card(6, -1, Location.MyHand, CardType.Creature, 2, 3, 2, Abilities.None, 0, 0, 0);
c[6] = new Card(7, -1, Location.MyHand, CardType.Creature, 2, 2, 2, Abilities.Ward, 0, 0, 0);
c[7] = new Card(8, -1, Location.MyHand, CardType.Creature, 2, 2, 3, Abilities.None, 0, 0, 0);
c[8] = new Card(9, -1, Location.MyHand, CardType.Creature, 3, 3, 4, Abilities.None, 0, 0, 0);
c[9] = new Card(10, -1, Location.MyHand, CardType.Creature, 3, 3, 1, Abilities.Drain, 0, 0, 0);
c[10] = new Card(11, -1, Location.MyHand, CardType.Creature, 3, 5, 2, Abilities.None, 0, 0, 0);
c[11] = new Card(12, -1, Location.MyHand, CardType.Creature, 3, 2, 5, Abilities.None, 0, 0, 0);
c[12] = new Card(13, -1, Location.MyHand, CardType.Creature, 4, 5, 3, Abilities.None, 1, -1, 0);
c[13] = new Card(14, -1, Location.MyHand, CardType.Creature, 4, 9, 1, Abilities.None, 0, 0, 0);
c[14] = new Card(15, -1, Location.MyHand, CardType.Creature, 4, 4, 5, Abilities.None, 0, 0, 0);
c[15] = new Card(16, -1, Location.MyHand, CardType.Creature, 4, 6, 2, Abilities.None, 0, 0, 0);
c[16] = new Card(17, -1, Location.MyHand, CardType.Creature, 4, 4, 5, Abilities.None, 0, 0, 0);
c[17] = new Card(18, -1, Location.MyHand, CardType.Creature, 4, 7, 4, Abilities.None, 0, 0, 0);
c[18] = new Card(19, -1, Location.MyHand, CardType.Creature, 5, 5, 6, Abilities.None, 0, 0, 0);
c[19] = new Card(20, -1, Location.MyHand, CardType.Creature, 5, 8, 2, Abilities.None, 0, 0, 0);
c[20] = new Card(21, -1, Location.MyHand, CardType.Creature, 5, 6, 5, Abilities.None, 0, 0, 0);
c[21] = new Card(22, -1, Location.MyHand, CardType.Creature, 6, 7, 5, Abilities.None, 0, 0, 0);
c[22] = new Card(23, -1, Location.MyHand, CardType.Creature, 7, 8, 8, Abilities.None, 0, 0, 0);
c[23] = new Card(24, -1, Location.MyHand, CardType.Creature, 1, 1, 1, Abilities.None, 0, -1, 0);
c[24] = new Card(25, -1, Location.MyHand, CardType.Creature, 2, 3, 1, Abilities.None, -2, -2, 0);
c[25] = new Card(26, -1, Location.MyHand, CardType.Creature, 2, 3, 2, Abilities.None, 0, -1, 0);
c[26] = new Card(27, -1, Location.MyHand, CardType.Creature, 2, 2, 2, Abilities.None, 2, 0, 0);
c[27] = new Card(28, -1, Location.MyHand, CardType.Creature, 2, 1, 2, Abilities.None, 0, 0, 1);
c[28] = new Card(29, -1, Location.MyHand, CardType.Creature, 2, 2, 1, Abilities.None, 0, 0, 1);
c[29] = new Card(30, -1, Location.MyHand, CardType.Creature, 3, 4, 2, Abilities.None, 0, -2, 0);
c[30] = new Card(31, -1, Location.MyHand, CardType.Creature, 3, 3, 1, Abilities.None, 0, -1, 0);
c[31] = new Card(32, -1, Location.MyHand, CardType.Creature, 3, 3, 2, Abilities.None, 0, 0, 1);
c[32] = new Card(33, -1, Location.MyHand, CardType.Creature, 4, 4, 3, Abilities.None, 0, 0, 1);
c[33] = new Card(34, -1, Location.MyHand, CardType.Creature, 5, 3, 5, Abilities.None, 0, 0, 1);
c[34] = new Card(35, -1, Location.MyHand, CardType.Creature, 6, 5, 2, Abilities.Breakthrough, 0, 0, 1);
c[35] = new Card(36, -1, Location.MyHand, CardType.Creature, 6, 4, 4, Abilities.None, 0, 0, 2);
c[36] = new Card(37, -1, Location.MyHand, CardType.Creature, 6, 5, 7, Abilities.None, 0, 0, 1);
c[37] = new Card(38, -1, Location.MyHand, CardType.Creature, 1, 1, 3, Abilities.Drain, 0, 0, 0);
c[38] = new Card(39, -1, Location.MyHand, CardType.Creature, 1, 2, 1, Abilities.Drain, 0, 0, 0);
c[39] = new Card(40, -1, Location.MyHand, CardType.Creature, 3, 2, 3, Abilities.Drain | Abilities.Guard, 0, 0, 0);
c[40] = new Card(41, -1, Location.MyHand, CardType.Creature, 3, 2, 2, Abilities.Charge | Abilities.Drain, 0, 0, 0);
c[41] = new Card(42, -1, Location.MyHand, CardType.Creature, 4, 4, 2, Abilities.Drain, 0, 0, 0);
c[42] = new Card(43, -1, Location.MyHand, CardType.Creature, 6, 5, 5, Abilities.Drain, 0, 0, 0);
c[43] = new Card(44, -1, Location.MyHand, CardType.Creature, 6, 3, 7, Abilities.Drain | Abilities.Lethal, 0, 0, 0);
c[44] = new Card(45, -1, Location.MyHand, CardType.Creature, 6, 6, 5, Abilities.Breakthrough | Abilities.Drain, -3, 0, 0);
c[45] = new Card(46, -1, Location.MyHand, CardType.Creature, 9, 7, 7, Abilities.Drain, 0, 0, 0);
c[46] = new Card(47, -1, Location.MyHand, CardType.Creature, 2, 1, 5, Abilities.Drain, 0, 0, 0);
c[47] = new Card(48, -1, Location.MyHand, CardType.Creature, 1, 1, 1, Abilities.Lethal, 0, 0, 0);
c[48] = new Card(49, -1, Location.MyHand, CardType.Creature, 2, 1, 2, Abilities.Guard | Abilities.Lethal, 0, 0, 0);
c[49] = new Card(50, -1, Location.MyHand, CardType.Creature, 3, 3, 2, Abilities.Lethal, 0, 0, 0);
c[50] = new Card(51, -1, Location.MyHand, CardType.Creature, 4, 3, 5, Abilities.Lethal, 0, 0, 0);
c[51] = new Card(52, -1, Location.MyHand, CardType.Creature, 4, 2, 4, Abilities.Lethal, 0, 0, 0);
c[52] = new Card(53, -1, Location.MyHand, CardType.Creature, 4, 1, 1, Abilities.Charge | Abilities.Lethal, 0, 0, 0);
c[53] = new Card(54, -1, Location.MyHand, CardType.Creature, 3, 2, 2, Abilities.Lethal, 0, 0, 0);
c[54] = new Card(55, -1, Location.MyHand, CardType.Creature, 2, 0, 5, Abilities.Guard, 0, 0, 0);
c[55] = new Card(56, -1, Location.MyHand, CardType.Creature, 4, 2, 7, Abilities.None, 0, 0, 0);
c[56] = new Card(57, -1, Location.MyHand, CardType.Creature, 4, 1, 8, Abilities.None, 0, 0, 0);
c[57] = new Card(58, -1, Location.MyHand, CardType.Creature, 6, 5, 6, Abilities.Breakthrough, 0, 0, 0);
c[58] = new Card(59, -1, Location.MyHand, CardType.Creature, 7, 7, 7, Abilities.None, 1, -1, 0);
c[59] = new Card(60, -1, Location.MyHand, CardType.Creature, 7, 4, 8, Abilities.None, 0, 0, 0);
c[60] = new Card(61, -1, Location.MyHand, CardType.Creature, 9, 10, 10, Abilities.None, 0, 0, 0);
c[61] = new Card(62, -1, Location.MyHand, CardType.Creature, 12, 12, 12, Abilities.Breakthrough | Abilities.Guard, 0, 0, 0);
c[62] = new Card(63, -1, Location.MyHand, CardType.Creature, 2, 0, 4, Abilities.Guard | Abilities.Ward, 0, 0, 0);
c[63] = new Card(64, -1, Location.MyHand, CardType.Creature, 2, 1, 1, Abilities.Guard | Abilities.Ward, 0, 0, 0);
c[64] = new Card(65, -1, Location.MyHand, CardType.Creature, 2, 2, 2, Abilities.Ward, 0, 0, 0);
c[65] = new Card(66, -1, Location.MyHand, CardType.Creature, 5, 5, 1, Abilities.Ward, 0, 0, 0);
c[66] = new Card(67, -1, Location.MyHand, CardType.Creature, 6, 5, 5, Abilities.Ward, 0, -2, 0);
c[67] = new Card(68, -1, Location.MyHand, CardType.Creature, 6, 7, 5, Abilities.Ward, 0, 0, 0);
c[68] = new Card(69, -1, Location.MyHand, CardType.Creature, 3, 4, 4, Abilities.Breakthrough, 0, 0, 0);
c[69] = new Card(70, -1, Location.MyHand, CardType.Creature, 4, 6, 3, Abilities.Breakthrough, 0, 0, 0);
c[70] = new Card(71, -1, Location.MyHand, CardType.Creature, 4, 3, 2, Abilities.Breakthrough | Abilities.Charge, 0, 0, 0);
c[71] = new Card(72, -1, Location.MyHand, CardType.Creature, 4, 5, 3, Abilities.Breakthrough, 0, 0, 0);
c[72] = new Card(73, -1, Location.MyHand, CardType.Creature, 4, 4, 4, Abilities.Breakthrough, 4, 0, 0);
c[73] = new Card(74, -1, Location.MyHand, CardType.Creature, 5, 5, 4, Abilities.Breakthrough | Abilities.Guard, 0, 0, 0);
c[74] = new Card(75, -1, Location.MyHand, CardType.Creature, 5, 6, 5, Abilities.Breakthrough, 0, 0, 0);
c[75] = new Card(76, -1, Location.MyHand, CardType.Creature, 6, 5, 5, Abilities.Breakthrough | Abilities.Drain, 0, 0, 0);
c[76] = new Card(77, -1, Location.MyHand, CardType.Creature, 7, 7, 7, Abilities.Breakthrough, 0, 0, 0);
c[77] = new Card(78, -1, Location.MyHand, CardType.Creature, 8, 5, 5, Abilities.Breakthrough, 0, -5, 0);
c[78] = new Card(79, -1, Location.MyHand, CardType.Creature, 8, 8, 8, Abilities.Breakthrough, 0, 0, 0);
c[79] = new Card(80, -1, Location.MyHand, CardType.Creature, 8, 8, 8, Abilities.Breakthrough | Abilities.Guard, 0, 0, 1);
c[80] = new Card(81, -1, Location.MyHand, CardType.Creature, 9, 6, 6, Abilities.Breakthrough | Abilities.Charge, 0, 0, 0);
c[81] = new Card(82, -1, Location.MyHand, CardType.Creature, 7, 5, 5, Abilities.Breakthrough | Abilities.Drain | Abilities.Ward, 0, 0, 0);
c[82] = new Card(83, -1, Location.MyHand, CardType.Creature, 0, 1, 1, Abilities.Charge, 0, 0, 0);
c[83] = new Card(84, -1, Location.MyHand, CardType.Creature, 2, 1, 1, Abilities.Charge | Abilities.Drain | Abilities.Ward, 0, 0, 0);
c[84] = new Card(85, -1, Location.MyHand, CardType.Creature, 3, 2, 3, Abilities.Charge, 0, 0, 0);
c[85] = new Card(86, -1, Location.MyHand, CardType.Creature, 3, 1, 5, Abilities.Charge, 0, 0, 0);
c[86] = new Card(87, -1, Location.MyHand, CardType.Creature, 4, 2, 5, Abilities.Charge | Abilities.Guard, 0, 0, 0);
c[87] = new Card(88, -1, Location.MyHand, CardType.Creature, 5, 4, 4, Abilities.Charge, 0, 0, 0);
c[88] = new Card(89, -1, Location.MyHand, CardType.Creature, 5, 4, 1, Abilities.Charge, 2, 0, 0);
c[89] = new Card(90, -1, Location.MyHand, CardType.Creature, 8, 5, 5, Abilities.Charge, 0, 0, 0);
c[90] = new Card(91, -1, Location.MyHand, CardType.Creature, 0, 1, 2, Abilities.Guard, 0, 1, 0);
c[91] = new Card(92, -1, Location.MyHand, CardType.Creature, 1, 0, 1, Abilities.Guard, 2, 0, 0);
c[92] = new Card(93, -1, Location.MyHand, CardType.Creature, 1, 2, 1, Abilities.Guard, 0, 0, 0);
c[93] = new Card(94, -1, Location.MyHand, CardType.Creature, 2, 1, 4, Abilities.Guard, 0, 0, 0);
c[94] = new Card(95, -1, Location.MyHand, CardType.Creature, 2, 2, 3, Abilities.Guard, 0, 0, 0);
c[95] = new Card(96, -1, Location.MyHand, CardType.Creature, 2, 3, 2, Abilities.Guard, 0, 0, 0);
c[96] = new Card(97, -1, Location.MyHand, CardType.Creature, 3, 3, 3, Abilities.Guard, 0, 0, 0);
c[97] = new Card(98, -1, Location.MyHand, CardType.Creature, 3, 2, 4, Abilities.Guard, 0, 0, 0);
c[98] = new Card(99, -1, Location.MyHand, CardType.Creature, 3, 2, 5, Abilities.Guard, 0, 0, 0);
c[99] = new Card(100, -1, Location.MyHand, CardType.Creature, 3, 1, 6, Abilities.Guard, 0, 0, 0);
c[100] = new Card(101, -1, Location.MyHand, CardType.Creature, 4, 3, 4, Abilities.Guard, 0, 0, 0);
c[101] = new Card(102, -1, Location.MyHand, CardType.Creature, 4, 3, 3, Abilities.Guard, 0, -1, 0);
c[102] = new Card(103, -1, Location.MyHand, CardType.Creature, 4, 3, 6, Abilities.Guard, 0, 0, 0);
c[103] = new Card(104, -1, Location.MyHand, CardType.Creature, 4, 4, 4, Abilities.Guard, 0, 0, 0);
c[104] = new Card(105, -1, Location.MyHand, CardType.Creature, 5, 4, 6, Abilities.Guard, 0, 0, 0);
c[105] = new Card(106, -1, Location.MyHand, CardType.Creature, 5, 5, 5, Abilities.Guard, 0, 0, 0);
c[106] = new Card(107, -1, Location.MyHand, CardType.Creature, 5, 3, 3, Abilities.Guard, 3, 0, 0);
c[107] = new Card(108, -1, Location.MyHand, CardType.Creature, 5, 2, 6, Abilities.Guard, 0, 0, 0);
c[108] = new Card(109, -1, Location.MyHand, CardType.Creature, 5, 5, 6, Abilities.None, 0, 0, 0);
c[109] = new Card(110, -1, Location.MyHand, CardType.Creature, 5, 0, 9, Abilities.Guard, 0, 0, 0);
c[110] = new Card(111, -1, Location.MyHand, CardType.Creature, 6, 6, 6, Abilities.Guard, 0, 0, 0);
c[111] = new Card(112, -1, Location.MyHand, CardType.Creature, 6, 4, 7, Abilities.Guard, 0, 0, 0);
c[112] = new Card(113, -1, Location.MyHand, CardType.Creature, 6, 2, 4, Abilities.Guard, 4, 0, 0);
c[113] = new Card(114, -1, Location.MyHand, CardType.Creature, 7, 7, 7, Abilities.Guard, 0, 0, 0);
c[114] = new Card(115, -1, Location.MyHand, CardType.Creature, 8, 5, 5, Abilities.Guard | Abilities.Ward, 0, 0, 0);
c[115] = new Card(116, -1, Location.MyHand, CardType.Creature, 12, 8, 8, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0);
c[116] = new Card(117, -1, Location.MyHand, CardType.GreenItem, 1, 1, 1, Abilities.Breakthrough, 0, 0, 0);
c[117] = new Card(118, -1, Location.MyHand, CardType.GreenItem, 0, 0, 3, Abilities.None, 0, 0, 0);
c[118] = new Card(119, -1, Location.MyHand, CardType.GreenItem, 1, 1, 2, Abilities.None, 0, 0, 0);
c[119] = new Card(120, -1, Location.MyHand, CardType.GreenItem, 2, 1, 0, Abilities.Lethal, 0, 0, 0);
c[120] = new Card(121, -1, Location.MyHand, CardType.GreenItem, 2, 0, 3, Abilities.None, 0, 0, 1);
c[121] = new Card(122, -1, Location.MyHand, CardType.GreenItem, 2, 1, 3, Abilities.Guard, 0, 0, 0);
c[122] = new Card(123, -1, Location.MyHand, CardType.GreenItem, 2, 4, 0, Abilities.None, 0, 0, 0);
c[123] = new Card(124, -1, Location.MyHand, CardType.GreenItem, 3, 2, 1, Abilities.Drain, 0, 0, 0);
c[124] = new Card(125, -1, Location.MyHand, CardType.GreenItem, 3, 1, 4, Abilities.None, 0, 0, 0);
c[125] = new Card(126, -1, Location.MyHand, CardType.GreenItem, 3, 2, 3, Abilities.None, 0, 0, 0);
c[126] = new Card(127, -1, Location.MyHand, CardType.GreenItem, 3, 0, 6, Abilities.None, 0, 0, 0);
c[127] = new Card(128, -1, Location.MyHand, CardType.GreenItem, 4, 4, 3, Abilities.None, 0, 0, 0);
c[128] = new Card(129, -1, Location.MyHand, CardType.GreenItem, 4, 2, 5, Abilities.None, 0, 0, 0);
c[129] = new Card(130, -1, Location.MyHand, CardType.GreenItem, 4, 0, 6, Abilities.None, 4, 0, 0);
c[130] = new Card(131, -1, Location.MyHand, CardType.GreenItem, 4, 4, 1, Abilities.None, 0, 0, 0);
c[131] = new Card(132, -1, Location.MyHand, CardType.GreenItem, 5, 3, 3, Abilities.Breakthrough, 0, 0, 0);
c[132] = new Card(133, -1, Location.MyHand, CardType.GreenItem, 5, 4, 0, Abilities.Ward, 0, 0, 0);
c[133] = new Card(134, -1, Location.MyHand, CardType.GreenItem, 4, 2, 2, Abilities.None, 0, 0, 1);
c[134] = new Card(135, -1, Location.MyHand, CardType.GreenItem, 6, 5, 5, Abilities.None, 0, 0, 0);
c[135] = new Card(136, -1, Location.MyHand, CardType.GreenItem, 0, 1, 1, Abilities.None, 0, 0, 0);
c[136] = new Card(137, -1, Location.MyHand, CardType.GreenItem, 2, 0, 0, Abilities.Ward, 0, 0, 0);
c[137] = new Card(138, -1, Location.MyHand, CardType.GreenItem, 2, 0, 0, Abilities.Guard, 0, 0, 1);
c[138] = new Card(139, -1, Location.MyHand, CardType.GreenItem, 4, 0, 0, Abilities.Lethal | Abilities.Ward, 0, 0, 0);
c[139] = new Card(140, -1, Location.MyHand, CardType.GreenItem, 2, 0, 0, Abilities.Charge, 0, 0, 0);
c[140] = new Card(141, -1, Location.MyHand, CardType.RedItem, 0, -1, -1, Abilities.None, 0, 0, 0);
c[141] = new Card(142, -1, Location.MyHand, CardType.RedItem, 0, 0, 0, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0);
c[142] = new Card(143, -1, Location.MyHand, CardType.RedItem, 0, 0, 0, Abilities.Guard, 0, 0, 0);
c[143] = new Card(144, -1, Location.MyHand, CardType.RedItem, 1, 0, -2, Abilities.None, 0, 0, 0);
c[144] = new Card(145, -1, Location.MyHand, CardType.RedItem, 3, -2, -2, Abilities.None, 0, 0, 0);
c[145] = new Card(146, -1, Location.MyHand, CardType.RedItem, 4, -2, -2, Abilities.None, 0, -2, 0);
c[146] = new Card(147, -1, Location.MyHand, CardType.RedItem, 2, 0, -1, Abilities.None, 0, 0, 1);
c[147] = new Card(148, -1, Location.MyHand, CardType.RedItem, 2, 0, -2, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0);
c[148] = new Card(149, -1, Location.MyHand, CardType.RedItem, 3, 0, 0, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 1);
c[149] = new Card(150, -1, Location.MyHand, CardType.RedItem, 2, 0, -3, Abilities.None, 0, 0, 0);
c[150] = new Card(151, -1, Location.MyHand, CardType.RedItem, 5, 0, -99, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0);
c[151] = new Card(152, -1, Location.MyHand, CardType.RedItem, 7, 0, -7, Abilities.None, 0, 0, 1);
c[152] = new Card(153, -1, Location.MyHand, CardType.BlueItem, 2, 0, 0, Abilities.None, 5, 0, 0);
c[153] = new Card(154, -1, Location.MyHand, CardType.BlueItem, 2, 0, 0, Abilities.None, 0, -2, 1);
c[154] = new Card(155, -1, Location.MyHand, CardType.BlueItem, 3, 0, -3, Abilities.None, 0, -1, 0);
c[155] = new Card(156, -1, Location.MyHand, CardType.BlueItem, 3, 0, 0, Abilities.None, 3, -3, 0);
c[156] = new Card(157, -1, Location.MyHand, CardType.BlueItem, 3, 0, -1, Abilities.None, 1, 0, 1);
c[157] = new Card(158, -1, Location.MyHand, CardType.BlueItem, 3, 0, -4, Abilities.None, 0, 0, 0);
c[158] = new Card(159, -1, Location.MyHand, CardType.BlueItem, 4, 0, -3, Abilities.None, 3, 0, 0);
c[159] = new Card(160, -1, Location.MyHand, CardType.BlueItem, 2, 0, 0, Abilities.None, 2, -2, 0);
return c;
}
private static string[] BuildNames()
{
return new[]
{
"Slimer",
"Scuttler",
"Beavrat",
"Plated Toad",
"Grime Gnasher",
"Murgling",
"Rootkin Sapling",
"Psyshroom",
"Corrupted Beavrat",
"Carnivorous Bush",
"Snowsaur",
"Woodshroom",
"Swamp Terror",
"Fanged Lunger",
"Pouncing Flailmouth",
"Wrangler Fish",
"Ash Walker",
"Acid Golem",
"Foulbeast",
"Hedge Demon",
"Crested Scuttler",
"Sigbovak",
"Titan Cave Hog",
"Exploding Skitterbug",
"Spiney Chompleaf",
"Razor Crab",
"Nut Gatherer",
"Infested Toad",
"Steelplume Nestling",
"Venomous Bog Hopper",
"Woodland Hunter",
"Sandsplat",
"Chameleskulk",
"Eldritch Cyclops",
"Snail-eyed Hulker",
"Possessed Skull",
"Eldritch Multiclops",
"Imp",
"Voracious Imp",
"Rock Gobbler",
"Blizzard Demon",
"Flying Leech",
"Screeching Nightmare",
"Deathstalker",
"Night Howler",
"Soul Devourer",
"Gnipper",
"Venom Hedgehog",
"Shiny Prowler",
"Puff Biter",
"Elite Bilespitter",
"Bilespitter",
"Possessed Abomination",
"Shadow Biter",
"Hermit Slime",
"Giant Louse",
"Dream-Eater",
"Darkscale Predator",
"Sea Ghost",
"Gritsuck Troll",
"Alpha Troll",
"Mutant Troll",
"Rootkin Drone",
"Coppershell Tortoise",
"Steelplume Defender",
"Staring Wickerbeast",
"Flailing Hammerhead",
"Giant Squid",
"Charging Boarhound",
"Murglord",
"Flying Murgling",
"Shuffling Nightmare",
"Bog Bounder",
"Crusher",
"Titan Prowler",
"Crested Chomper",
"Lumbering Giant",
"Shambler",
"Scarlet Colossus",
"Corpse Guzzler",
"Flying Corpse Guzzler",
"Slithering Nightmare",
"Restless Owl",
"Fighter Tick",
"Heartless Crow",
"Crazed Nose-pincher",
"Bloat Demon",
"Abyss Nightmare",
"Boombeak",
"Eldritch Swooper",
"Flumpy",
"Wurm",
"Spinekid",
"Rootkin Defender",
"Wildum",
"Prairie Protector",
"Turta",
"Lilly Hopper",
"Cave Crab",
"Stalagopod",
"Engulfer",
"Mole Demon",
"Mutating Rootkin",
"Deepwater Shellcrab",
"King Shellcrab",
"Far-reaching Nightmare",
"Worker Shellcrab",
"Rootkin Elder",
"Elder Engulfer",
"Gargoyle",
"Turta Knight",
"Rootkin Leader",
"Tamed Bilespitter",
"Gargantua",
"Rootkin Warchief",
"Emperor Nightmare",
"Protein",
"Royal Helm",
"Serrated Shield",
"Venomfruit",
"Enchanted Hat",
"Bolstering Bread",
"Wristguards",
"Blood Grapes",
"Healthy Veggies",
"Heavy Shield",
"Imperial Helm",
"Enchanted Cloth",
"Enchanted Leather",
"Helm of Remedy",
"Heavy Gauntlet",
"High Protein",
"Pie of Power",
"Light The Way",
"Imperial Armour",
"Buckler",
"Ward",
"Grow Horns",
"Grow Stingers",
"Grow Wings",
"Throwing Knife",
"Staff of Suppression",
"Pierce Armour",
"Rune Axe",
"Cursed Sword",
"Cursed Scimitar",
"Quick Shot",
"Helm Crusher",
"Rootkin Ritual",
"Throwing Axe",
"Decimate",
"Mighty Throwing Axe",
"Healing Potion",
"Poison",
"Scroll of Firebolt",
"Major Life Steal Potion",
"Life Sap Drop",
"Tome of Thunder",
"Vial of Soul Drain",
"Minor Life Steal Potion",
};
}
}
}

// ===== src/LocmBot/Game/InputParser.cs =====
namespace Locm
{
public static class InputParser
{
public static TurnInput ReadTurn(TextReader reader)
{
string first = reader.ReadLine();
if (first == null) return null;
return ReadTurn(first, reader);
}
public static TurnInput ReadTurn(string firstLine, TextReader reader)
{
var t = new TurnInput();
t.Me = ParsePlayer(firstLine);
t.Opponent = ParsePlayer(ReadRequired(reader));
var oppLine = Split(ReadRequired(reader));
t.OpponentHandSize = int.Parse(oppLine[0]);
int opponentActions = int.Parse(oppLine[1]);
for (int i = 0; i < opponentActions; i++)
{
string line = ReadRequired(reader);
int space = line.IndexOf(' ');
t.OpponentActions.Add(new OpponentAction
{
CardNumber = int.Parse(space < 0 ? line : line.Substring(0, space)),
Action = space < 0 ? "" : line.Substring(space + 1),
});
}
int cardCount = int.Parse(ReadRequired(reader).Trim());
for (int i = 0; i < cardCount; i++)
t.Cards.Add(ParseCard(ReadRequired(reader)));
return t;
}
public static PlayerInfo ParsePlayer(string line)
{
var p = Split(line);
return new PlayerInfo
{
Health = int.Parse(p[0]),
Mana = int.Parse(p[1]),
DeckSize = int.Parse(p[2]),
Rune = int.Parse(p[3]),
Draw = int.Parse(p[4]),
};
}
public static Card ParseCard(string line)
{
var p = Split(line);
return new Card(
number: int.Parse(p[0]),
instanceId: int.Parse(p[1]),
location: (Location)int.Parse(p[2]),
type: (CardType)int.Parse(p[3]),
cost: int.Parse(p[4]),
attack: int.Parse(p[5]),
defense: int.Parse(p[6]),
abilities: AbilitiesExt.Parse(p[7]),
myHealthChange: int.Parse(p[8]),
opponentHealthChange: int.Parse(p[9]),
cardDraw: int.Parse(p[10]));
}
private static string[] Split(string line) =>
line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
private static string ReadRequired(TextReader reader)
{
string line = reader.ReadLine();
if (line == null) throw new EndOfStreamException("Unexpected end of input");
return line;
}
}
}

// ===== src/LocmBot/Game/TurnInput.cs =====
namespace Locm
{
public struct PlayerInfo
{
public int Health;
public int Mana;
public int DeckSize;
public int Rune;
public int Draw;
public override string ToString() => $"hp={Health} mana={Mana} deck={DeckSize} rune={Rune} draw={Draw}";
}
public struct OpponentAction
{
public int CardNumber;
public string Action;
public override string ToString() => $"{CardNumber} {Action}";
}
public sealed class TurnInput
{
public PlayerInfo Me;
public PlayerInfo Opponent;
public int OpponentHandSize;
public List<OpponentAction> OpponentActions = new List<OpponentAction>();
public List<Card> Cards = new List<Card>();
public bool LooksLikeDraft
{
get
{
if (Me.Mana != 0 || Cards.Count != 3) return false;
foreach (var c in Cards)
if (c.Location != Location.MyHand) return false;
return true;
}
}
}
}

// ===== src/LocmBot/Program.cs =====
namespace Locm
{
public static class Program
{
private const bool DumpInput = false;
public static void Main(string[] args)
{
var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
var stderr = Console.Error;
var search = new SearchBattle();
Tuning.Apply(args, search, stderr);
var bot = new Bot(new RatingDraft(), search, stderr, DumpInput ? stderr : null);
bot.Run(Console.In, stdout);
}
}
}

// ===== src/LocmBot/Sim/Creature.cs =====
namespace Locm
{
public struct Creature
{
public Card Card;
public int Attack;
public int Defense;
public Abilities Abilities;
public bool CanAttack;
public bool HasAttacked;
public int MyHealthChange;
public int OpponentHealthChange;
public int CardDraw;
public int InstanceId => Card.InstanceId;
public int BaseId => Card.Number;
public bool Has(Abilities flag) => (Abilities & flag) != 0;
public static Creature Summon(Card card)
{
return new Creature
{
Card = card,
Attack = card.Attack,
Defense = card.Defense,
Abilities = card.Abilities,
CanAttack = (card.Abilities & Abilities.Charge) != 0,
HasAttacked = false,
MyHealthChange = card.MyHealthChange,
OpponentHealthChange = card.OpponentHealthChange,
CardDraw = card.CardDraw,
};
}
public static Creature FromInput(Card card, bool canAttack)
{
return new Creature
{
Card = card,
Attack = card.Attack,
Defense = card.Defense,
Abilities = card.Abilities,
CanAttack = canAttack,
HasAttacked = false,
MyHealthChange = card.MyHealthChange,
OpponentHealthChange = card.OpponentHealthChange,
CardDraw = card.CardDraw,
};
}
public void ClearSummonEffects()
{
MyHealthChange = 0;
OpponentHealthChange = 0;
CardDraw = 0;
}
public string ToInputLine(bool opponentBoard)
{
int loc = opponentBoard ? -1 : 1;
return $"{Card.Number} {Card.InstanceId} {loc} 0 {Card.Cost} {Attack} {Defense} {Abilities.Format()} {MyHealthChange} {OpponentHealthChange} {CardDraw}";
}
public override string ToString() =>
$"#{BaseId}/{InstanceId} {Attack}/{Defense} {Abilities.Format()}{(CanAttack ? " ready" : "")}{(HasAttacked ? " attacked" : "")}";
}
}

// ===== src/LocmBot/Sim/GameAction.cs =====
namespace Locm
{
public enum ActionType : byte
{
Pass = 0,
Summon = 1,
Attack = 2,
Use = 3,
}
public readonly struct GameAction : IEquatable<GameAction>
{
public readonly ActionType Type;
public readonly int Id;
public readonly int Target;
public const int Face = -1;
public GameAction(ActionType type, int id, int target)
{
Type = type;
Id = id;
Target = target;
}
public static readonly GameAction Pass = new GameAction(ActionType.Pass, 0, 0);
public static GameAction Summon(int id) => new GameAction(ActionType.Summon, id, 0);
public static GameAction Attack(int id, int target) => new GameAction(ActionType.Attack, id, target);
public static GameAction Use(int id, int target) => new GameAction(ActionType.Use, id, target);
public bool IsPass => Type == ActionType.Pass;
public override string ToString()
{
switch (Type)
{
case ActionType.Summon: return "SUMMON " + Id;
case ActionType.Attack: return "ATTACK " + Id + " " + Target;
case ActionType.Use: return "USE " + Id + " " + Target;
default: return "PASS";
}
}
public static string Format(IList<GameAction> actions)
{
if (actions == null || actions.Count == 0) return "PASS";
var sb = new StringBuilder();
for (int i = 0; i < actions.Count; i++)
{
if (i > 0) sb.Append(';');
sb.Append(actions[i].ToString());
}
return sb.ToString();
}
public static GameAction Parse(string s)
{
GameAction a;
if (!TryParse(s, out a)) throw new FormatException("invalid action: '" + s + "'");
return a;
}
public static bool TryParse(string s, out GameAction action)
{
action = Pass;
if (s == null) return false;
var p = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
if (p.Length == 0) return false;
int id, target;
switch (p[0])
{
case "PASS":
return true;
case "SUMMON":
if (p.Length < 2 || !int.TryParse(p[1], out id)) return false;
action = Summon(id);
return true;
case "ATTACK":
case "USE":
if (p.Length < 3 || !int.TryParse(p[1], out id) || !int.TryParse(p[2], out target)) return false;
action = p[0] == "ATTACK" ? Attack(id, target) : Use(id, target);
return true;
default:
return false;
}
}
public static List<GameAction> ParseSequence(string line)
{
var result = new List<GameAction>();
if (line == null) return result;
foreach (var part in line.Split(';'))
{
string s = part.Trim();
if (s.Length == 0) continue;
result.Add(Parse(s));
}
return result;
}
public bool Equals(GameAction o) => Type == o.Type && Id == o.Id && Target == o.Target;
public override bool Equals(object obj) => obj is GameAction && Equals((GameAction)obj);
public override int GetHashCode() => ((int)Type * 397 + Id) * 397 + Target;
public static bool operator ==(GameAction a, GameAction b) => a.Equals(b);
public static bool operator !=(GameAction a, GameAction b) => !a.Equals(b);
}
}

// ===== src/LocmBot/Sim/GameState.cs =====
namespace Locm
{
public sealed class GameState
{
public const int MaxHand = 8;
public const int MaxBoard = 6;
public const int MaxMana = 12;
public const int InitialHealth = 30;
public const int PlayerTurnLimit = 50;
public readonly PlayerState[] Players = { new PlayerState(), new PlayerState() };
public int Current;
public int Winner = -1;
public int Turn;
public PlayerState Me => Players[Current];
public PlayerState Opp => Players[1 - Current];
public bool IsOver => Winner >= 0;
public static int RefereeTurn(int myBattleTurn, bool secondPlayer) => 2 * myBattleTurn + (secondPlayer ? 1 : 0);
public static bool IsSecondPlayer(TurnInput input)
{
foreach (var c in input.Cards)
if (c.Location != Location.OpponentBoard) return c.InstanceId % 2 == 0;
foreach (var c in input.Cards)
return c.InstanceId % 2 != 0;
return false;
}
public static GameState FromInput(TurnInput input) => FromInput(input, 0);
public static GameState FromInput(TurnInput input, int turn) => new GameState().Load(input, turn);
public GameState Load(TurnInput input, int turn)
{
var s = this;
var me = s.Players[0];
var opp = s.Players[1];
me.Reset();
opp.Reset();
s.Winner = -1;
me.Health = input.Me.Health;
me.MaxMana = input.Me.Mana;
me.Mana = input.Me.Mana;
me.DeckSize = input.Me.DeckSize;
me.NextRune = input.Me.Rune;
me.DrawShown = input.Me.Draw;
me.NextTurnDraw = 1;
opp.Health = input.Opponent.Health;
opp.MaxMana = input.Opponent.Mana;
opp.Mana = 0;
opp.DeckSize = input.Opponent.DeckSize;
opp.NextRune = input.Opponent.Rune;
opp.DrawShown = input.Opponent.Draw;
opp.NextTurnDraw = input.Opponent.Draw;
opp.HandCount = input.OpponentHandSize;
foreach (var c in input.Cards)
{
switch (c.Location)
{
case Location.MyHand: me.AddHandCard(c); break;
case Location.MyBoard: me.AddCreature(Creature.FromInput(c, true)); break;
case Location.OpponentBoard: opp.AddCreature(Creature.FromInput(c, false)); break;
}
}
s.Current = 0;
s.Turn = turn;
s.CheckWinCondition();
return s;
}
public ulong Hash()
{
ulong h = Mix((ulong)(uint)(Current | (Winner + 1) << 2 | Turn << 4));
for (int p = 0; p < 2; p++)
{
var pl = Players[p];
ulong salt = (ulong)(p + 1) * 0x9E3779B97F4A7C15UL;
h ^= Mix(salt ^ (ulong)(uint)(pl.Health & 0xFFFF | (pl.Mana & 0xFF) << 16 | (pl.MaxMana & 0xFF) << 24));
h ^= Mix(salt + 1 ^ (ulong)(uint)(pl.DeckSize & 0xFF | (pl.NextRune & 0xFF) << 8 | (pl.NextTurnDraw & 0xFF) << 16 | (pl.HandCount & 0xFF) << 24));
for (int i = 0; i < pl.HandKnown; i++)
h ^= Mix(salt + 2 ^ (ulong)(uint)pl.Hand[i].InstanceId);
for (int i = 0; i < pl.BoardCount; i++)
{
ref Creature c = ref pl.Board[i];
ulong packed = (ulong)(uint)(c.InstanceId & 0xFF | (c.Attack & 0xFF) << 8 | (c.Defense & 0xFF) << 16 | (int)c.Abilities << 24)
| (c.CanAttack ? 1UL << 32 : 0) | (c.HasAttacked ? 1UL << 33 : 0);
h ^= Mix(salt + 3 ^ packed);
}
}
return h;
}
private static ulong Mix(ulong z)
{
z += 0x9E3779B97F4A7C15UL;
z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
return z ^ (z >> 31);
}
public void CopyFrom(GameState o)
{
Players[0].CopyFrom(o.Players[0]);
Players[1].CopyFrom(o.Players[1]);
Current = o.Current;
Winner = o.Winner;
Turn = o.Turn;
}
public GameState Clone()
{
var s = new GameState();
s.CopyFrom(this);
return s;
}
public void LegalActions(List<GameAction> into)
{
into.Clear();
var me = Me;
var opp = Opp;
if (me.BoardCount < MaxBoard)
{
for (int i = 0; i < me.HandKnown; i++)
{
if (me.Hand[i].Type != CardType.Creature || me.Hand[i].Cost > me.Mana) continue;
into.Add(GameAction.Summon(me.Hand[i].InstanceId));
}
}
bool guards = opp.HasGuard();
for (int i = 0; i < me.BoardCount; i++)
{
if (!me.Board[i].CanAttack) continue;
int id = me.Board[i].InstanceId;
if (guards)
{
for (int j = 0; j < opp.BoardCount; j++)
if (opp.Board[j].Has(Abilities.Guard)) into.Add(GameAction.Attack(id, opp.Board[j].InstanceId));
}
else
{
into.Add(GameAction.Attack(id, GameAction.Face));
for (int j = 0; j < opp.BoardCount; j++)
into.Add(GameAction.Attack(id, opp.Board[j].InstanceId));
}
}
for (int i = 0; i < me.HandKnown; i++)
{
Card c = me.Hand[i];
if (c.Type == CardType.Creature || c.Cost > me.Mana) continue;
if (c.Type == CardType.GreenItem)
{
for (int j = 0; j < me.BoardCount; j++)
into.Add(GameAction.Use(c.InstanceId, me.Board[j].InstanceId));
}
else
{
for (int j = 0; j < opp.BoardCount; j++)
into.Add(GameAction.Use(c.InstanceId, opp.Board[j].InstanceId));
if (c.Type == CardType.BlueItem) into.Add(GameAction.Use(c.InstanceId, GameAction.Face));
}
}
into.Add(GameAction.Pass);
}
public bool IsLegal(GameAction a)
{
var me = Me;
var opp = Opp;
switch (a.Type)
{
case ActionType.Pass:
return true;
case ActionType.Summon:
{
if (me.BoardCount >= MaxBoard) return false;
int i = me.FindHand(a.Id);
return i >= 0 && me.Hand[i].Type == CardType.Creature && me.Hand[i].Cost <= me.Mana;
}
case ActionType.Attack:
{
int ai = me.FindCreature(a.Id);
if (ai < 0 || !me.Board[ai].CanAttack) return false;
bool guards = opp.HasGuard();
if (a.Target == GameAction.Face) return !guards;
int di = opp.FindCreature(a.Target);
if (di < 0) return false;
return !guards || opp.Board[di].Has(Abilities.Guard);
}
case ActionType.Use:
{
int i = me.FindHand(a.Id);
if (i < 0) return false;
Card c = me.Hand[i];
if (c.Type == CardType.Creature || c.Cost > me.Mana) return false;
if (c.Type == CardType.GreenItem) return a.Target != GameAction.Face && me.FindCreature(a.Target) >= 0;
if (a.Target == GameAction.Face) return c.Type == CardType.BlueItem;
return opp.FindCreature(a.Target) >= 0;
}
}
return false;
}
public bool TryApply(GameAction a)
{
if (!IsLegal(a)) return false;
Apply(a);
return true;
}
public int ApplySequence(IList<GameAction> actions)
{
int illegal = 0;
for (int i = 0; i < actions.Count; i++)
{
if (IsOver) break;
if (actions[i].IsPass) continue;
if (!TryApply(actions[i])) illegal++;
}
return illegal;
}
public void Apply(GameAction a)
{
var me = Me;
var opp = Opp;
switch (a.Type)
{
case ActionType.Pass:
return;
case ActionType.Summon:
{
int hi = me.FindHand(a.Id);
if (hi < 0) throw Illegal(a, "card not in hand");
Card c = me.Hand[hi];
me.RemoveHand(hi);
me.Mana -= c.Cost;
me.AddCreature(Creature.Summon(c));
me.ModifyHealth(c.MyHealthChange);
opp.ModifyHealth(c.OpponentHealthChange);
me.NextTurnDraw += c.CardDraw;
break;
}
case ActionType.Attack:
{
int ai = me.FindCreature(a.Id);
if (ai < 0) throw Illegal(a, "attacker not on board");
Creature att = me.Board[ai];
if (a.Target == GameAction.Face)
{
att.CanAttack = false;
att.HasAttacked = true;
att.ClearSummonEffects();
me.Board[ai] = att;
me.ModifyHealth(att.Has(Abilities.Drain) ? att.Attack : 0);
opp.ModifyHealth(-att.Attack);
}
else
{
int di = opp.FindCreature(a.Target);
if (di < 0) throw Illegal(a, "defender not on board");
Creature def = opp.Board[di];
bool attDied, defDied;
int healthGain, healthTaken;
ResolveAttack(ref att, ref def, out attDied, out defDied, out healthGain, out healthTaken);
if (defDied) opp.RemoveCreature(di); else opp.Board[di] = def;
if (attDied) me.RemoveCreature(ai); else me.Board[ai] = att;
me.ModifyHealth(healthGain);
opp.ModifyHealth(healthTaken);
}
break;
}
case ActionType.Use:
{
int hi = me.FindHand(a.Id);
if (hi < 0) throw Illegal(a, "item not in hand");
Card item = me.Hand[hi];
me.RemoveHand(hi);
me.Mana -= item.Cost;
if (item.Type == CardType.GreenItem)
{
int ti = me.FindCreature(a.Target);
if (ti < 0) throw Illegal(a, "target not on my board");
Creature t = me.Board[ti];
bool died = ResolveUse(item, ref t);
if (!died) me.Board[ti] = t;
me.ModifyHealth(item.MyHealthChange);
opp.ModifyHealth(item.OpponentHealthChange);
}
else if (a.Target == GameAction.Face)
{
me.ModifyHealth(item.MyHealthChange);
opp.ModifyHealth(item.Defense + item.OpponentHealthChange);
}
else
{
int ti = opp.FindCreature(a.Target);
if (ti < 0) throw Illegal(a, "target not on opponent board");
Creature t = opp.Board[ti];
bool died = ResolveUse(item, ref t);
if (died) opp.RemoveCreature(ti); else opp.Board[ti] = t;
me.ModifyHealth(item.MyHealthChange);
opp.ModifyHealth(item.OpponentHealthChange);
}
me.NextTurnDraw += item.CardDraw;
break;
}
}
CheckWinCondition();
}
private static InvalidOperationException Illegal(GameAction a, string why) =>
new InvalidOperationException("illegal action " + a + ": " + why);
public static void ResolveAttack(ref Creature att, ref Creature def,
out bool attackerDied, out bool defenderDied, out int healthGain, out int healthTaken)
{
Creature a0 = att;
Creature d0 = def;
att.CanAttack = false;
att.HasAttacked = true;
att.ClearSummonEffects();
def.ClearSummonEffects();
if (d0.Has(Abilities.Ward)) SetWard(ref def, a0.Attack == 0);
if (a0.Has(Abilities.Ward)) SetWard(ref att, d0.Attack == 0);
int damageGiven = d0.Has(Abilities.Ward) ? 0 : a0.Attack;
int damageTaken = a0.Has(Abilities.Ward) ? 0 : d0.Attack;
healthGain = 0;
healthTaken = 0;
defenderDied = damageGiven >= d0.Defense;
if (a0.Has(Abilities.Breakthrough) && defenderDied) healthTaken = d0.Defense - damageGiven;
if (a0.Has(Abilities.Lethal) && damageGiven > 0) defenderDied = true;
if (a0.Has(Abilities.Drain) && damageGiven > 0) healthGain = a0.Attack;
if (!defenderDied) def.Defense -= damageGiven;
attackerDied = damageTaken >= a0.Defense;
if (d0.Has(Abilities.Lethal) && damageTaken > 0) attackerDied = true;
if (!attackerDied) att.Defense -= damageTaken;
}
public static bool ResolveUse(Card item, ref Creature t)
{
t.ClearSummonEffects();
if (item.Type == CardType.GreenItem)
{
t.Abilities |= item.Abilities;
if ((item.Abilities & Abilities.Charge) != 0) t.CanAttack = !t.HasAttacked;
}
else
{
t.Abilities &= ~item.Abilities;
}
t.Attack = Math.Max(0, t.Attack + item.Attack);
if (t.Has(Abilities.Ward) && item.Defense < 0)
t.Abilities &= ~Abilities.Ward;
else
t.Defense += item.Defense;
return t.Defense <= 0;
}
private static void SetWard(ref Creature c, bool on)
{
if (on) c.Abilities |= Abilities.Ward; else c.Abilities &= ~Abilities.Ward;
}
public void CheckWinCondition()
{
if (Opp.Health <= 0) Winner = Current;
else if (Me.Health <= 0) Winner = 1 - Current;
}
public void EndTurn()
{
CheckWinCondition();
var prev = Me;
for (int i = 0; i < prev.BoardCount; i++)
{
prev.Board[i].CanAttack = false;
prev.Board[i].HasAttacked = false;
}
prev.DrawShown = prev.NextTurnDraw;
Current = 1 - Current;
Turn++;
var p = Me;
if (p.MaxMana < MaxMana + (p.BonusManaTurns > 0 ? 1 : 0))
p.MaxMana++;
if (p.BonusManaTurns > 0 && p.Mana == 0)
{
p.BonusManaTurns--;
if (p.BonusManaTurns == 0) p.MaxMana--;
}
p.Mana = p.MaxMana;
for (int i = 0; i < p.BoardCount; i++)
p.Board[i].CanAttack = true;
p.DrawCards(p.NextTurnDraw, Turn / 2);
p.DrawShown = p.NextTurnDraw;
p.NextTurnDraw = 1;
CheckWinCondition();
}
public string[] ToInputLines()
{
var me = Me;
var opp = Opp;
var lines = new List<string>(4 + me.HandKnown + me.BoardCount + opp.BoardCount);
lines.Add(me.ToInputLine());
lines.Add(opp.ToInputLine());
lines.Add(opp.HandCount + " 0");
lines.Add((me.HandKnown + me.BoardCount + opp.BoardCount).ToString());
for (int i = 0; i < me.HandKnown; i++) lines.Add(me.Hand[i].ToInputLine(Location.MyHand));
for (int i = 0; i < me.BoardCount; i++) lines.Add(me.Board[i].ToInputLine(false));
for (int i = 0; i < opp.BoardCount; i++) lines.Add(opp.Board[i].ToInputLine(true));
return lines.ToArray();
}
public override string ToString()
{
var sb = new StringBuilder();
sb.Append("turn ").Append(Turn).Append(" current P").Append(Current);
if (IsOver) sb.Append(" winner P").Append(Winner);
sb.AppendLine();
for (int p = 0; p < 2; p++)
{
var pl = Players[p];
sb.Append("P").Append(p).Append(": ").Append(pl).AppendLine();
for (int i = 0; i < pl.HandKnown; i++) sb.Append("   hand ").Append(pl.Hand[i]).AppendLine();
for (int i = 0; i < pl.BoardCount; i++) sb.Append("   board ").Append(pl.Board[i]).AppendLine();
}
return sb.ToString();
}
}
}

// ===== src/LocmBot/Sim/PlayerState.cs =====
namespace Locm
{
public sealed class PlayerState
{
public int Health;
public int MaxMana;
public int Mana;
public int DeckSize;
public int NextRune;
public int NextTurnDraw;
public int DrawShown;
public int BonusManaTurns;
public int HandCount;
public int HandKnown;
public readonly Card[] Hand = new Card[GameState.MaxHand];
public int BoardCount;
public readonly Creature[] Board = new Creature[GameState.MaxBoard];
public void Reset()
{
Health = 0; MaxMana = 0; Mana = 0; DeckSize = 0; NextRune = 0; NextTurnDraw = 0; DrawShown = 0; BonusManaTurns = 0;
HandCount = 0; HandKnown = 0; BoardCount = 0;
}
public void CopyFrom(PlayerState o)
{
Health = o.Health;
MaxMana = o.MaxMana;
Mana = o.Mana;
DeckSize = o.DeckSize;
NextRune = o.NextRune;
NextTurnDraw = o.NextTurnDraw;
DrawShown = o.DrawShown;
BonusManaTurns = o.BonusManaTurns;
HandCount = o.HandCount;
HandKnown = o.HandKnown;
Array.Copy(o.Hand, Hand, o.HandKnown);
BoardCount = o.BoardCount;
Array.Copy(o.Board, Board, o.BoardCount);
}
public void ModifyHealth(int mod)
{
Health += mod;
if (mod >= 0) return;
while (NextRune > 0 && Health <= NextRune)
{
NextTurnDraw++;
NextRune -= 5;
}
}
public void DrawCards(int n, int playerTurn)
{
for (int i = 0; i < n; i++)
{
if (DeckSize == 0 || playerTurn >= GameState.PlayerTurnLimit)
{
SuicideRunes();
continue;
}
if (HandCount >= GameState.MaxHand) break;
DeckSize--;
HandCount++;
}
}
private void SuicideRunes()
{
if (NextRune > 0)
{
Health = NextRune;
NextRune -= 5;
}
else
{
Health = 0;
}
}
public int FindHand(int instanceId)
{
for (int i = 0; i < HandKnown; i++)
if (Hand[i].InstanceId == instanceId) return i;
return -1;
}
public int FindCreature(int instanceId)
{
for (int i = 0; i < BoardCount; i++)
if (Board[i].InstanceId == instanceId) return i;
return -1;
}
public bool HasGuard()
{
for (int i = 0; i < BoardCount; i++)
if ((Board[i].Abilities & Abilities.Guard) != 0) return true;
return false;
}
public void AddHandCard(Card c)
{
if (HandKnown >= Hand.Length) throw new InvalidOperationException("hand overflow");
Hand[HandKnown++] = c;
HandCount++;
}
public void RevealHandCard(Card c)
{
if (HandKnown >= Hand.Length) throw new InvalidOperationException("hand overflow");
Hand[HandKnown++] = c;
if (HandCount < HandKnown) HandCount = HandKnown;
}
public void RemoveHand(int index)
{
for (int i = index + 1; i < HandKnown; i++) Hand[i - 1] = Hand[i];
HandKnown--;
HandCount--;
}
public void AddCreature(Creature c)
{
if (BoardCount >= Board.Length) throw new InvalidOperationException("board overflow");
Board[BoardCount++] = c;
}
public void RemoveCreature(int index)
{
for (int i = index + 1; i < BoardCount; i++) Board[i - 1] = Board[i];
BoardCount--;
}
public string ToInputLine() => $"{Health} {MaxMana} {DeckSize} {NextRune} {DrawShown}";
public override string ToString() =>
$"hp={Health} mana={Mana}/{MaxMana} deck={DeckSize} rune={NextRune} draw={NextTurnDraw} hand={HandKnown}/{HandCount} board={BoardCount}";
}
}

// ===== src/LocmBot/Tuning.cs =====
namespace Locm
{
public static class Tuning
{
public static void Apply(string[] args, SearchBattle search, TextWriter log)
{
foreach (var arg in args)
{
int eq = arg.IndexOf('=');
if (eq <= 0) continue;
string key = arg.Substring(0, eq).ToLowerInvariant();
var e = search.Eval;
if (key.StartsWith("o_"))
{
if (ReferenceEquals(search.OppEval, search.Eval)) search.OppEval = Clone(search.Eval);
e = search.OppEval;
key = key.Substring(2);
}
if (key == "curve")
{
var parts = arg.Substring(eq + 1).Split(',');
if (parts.Length == 8)
{
var curve = new double[8];
bool ok = true;
for (int i = 0; i < 8; i++) ok &= double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out curve[i]);
if (ok) RatingDraft.TargetCurve = curve;
}
continue;
}
double v;
if (!double.TryParse(arg.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
switch (key)
{
case "hp": e.HpW = v; break;
case "lowhp": e.LowHpW = v; break;
case "lowhpat": e.LowHp = (int)v; break;
case "midhp": e.MidHpW = v; break;
case "midhpat": e.MidHp = (int)v; break;
case "atk": e.AttackW = v; break;
case "def": e.DefenseW = v; break;
case "guard": e.GuardW = v; break;
case "guarddef": e.GuardDefW = v; break;
case "ward": e.WardW = v; break;
case "wardatk": e.WardAtkW = v; break;
case "lethal": e.LethalW = v; break;
case "drain": e.DrainAtkW = v; break;
case "breakthrough": e.BreakthroughAtkW = v; break;
case "charge": e.ChargeW = v; break;
case "hand": e.HandCardW = v; break;
case "handrating": e.HandRatingW = v; break;
case "fragile1": e.Fragile1W = v; break;
case "fragile2": e.Fragile2W = v; break;
case "blue": e.BlueHandW = v; break;
case "oppdraw": e.OppDrawW = v; break;
case "mydraw": e.MyDrawW = v; break;
case "reply": search.ReplyWeight = v; break;
case "cand": search.MaxCandidates = (int)v; break;
case "deep": search.DeepReplyCandidates = (int)v; break;
case "deepnodes": search.DeepReplyNodes = (int)v; break;
case "counter": search.CounterCandidates = (int)v; break;
case "net": search.UseNet = v != 0; break;
case "netscale": search.NetScale = v; break;
case "netadd": search.NetAdditive = v != 0; break;
case "counternodes": search.CounterNodes = (int)v; break;
case "table": CardRating.UseTable = v != 0; break;
case "curvew": RatingDraft.CurveW = v; break;
case "draftwin": CardTable.UseWinAdjusted = v != 0; break;
case "maxitems": RatingDraft.MaxItems = (int)v; break;
case "itempenalty": RatingDraft.ItemOverPenalty = v; break;
case "samecard": RatingDraft.SameCardPenalty = v; break;
default:
if (log != null) log.WriteLine("unknown override: " + arg);
continue;
}
if (log != null) log.WriteLine("override " + (ReferenceEquals(e, search.Eval) ? "" : "o_") + key + "=" + v.ToString(CultureInfo.InvariantCulture));
}
}
public static Evaluator Clone(Evaluator e)
{
return new Evaluator
{
AttackW = e.AttackW, DefenseW = e.DefenseW, GuardW = e.GuardW, GuardDefW = e.GuardDefW, WardW = e.WardW, WardAtkW = e.WardAtkW,
LethalW = e.LethalW, DrainAtkW = e.DrainAtkW, BreakthroughAtkW = e.BreakthroughAtkW, ChargeW = e.ChargeW,
Fragile1W = e.Fragile1W, Fragile2W = e.Fragile2W, BlueHandW = e.BlueHandW, HpW = e.HpW, LowHpW = e.LowHpW, LowHp = e.LowHp, MidHpW = e.MidHpW, MidHp = e.MidHp,
HandCardW = e.HandCardW, HandRatingW = e.HandRatingW, OppDrawW = e.OppDrawW, MyDrawW = e.MyDrawW,
};
}
}
}

// ===== src/LocmBot/TurnClock.cs =====
namespace Locm
{
public sealed class TurnClock
{
private readonly Stopwatch _sw = Stopwatch.StartNew();
public readonly int BudgetMs;
public TurnClock(int budgetMs) { BudgetMs = budgetMs; }
public long ElapsedMs => _sw.ElapsedMilliseconds;
public long RemainingMs => BudgetMs - _sw.ElapsedMilliseconds;
public bool TimeUp => _sw.ElapsedMilliseconds >= BudgetMs;
}
public static class TimeLimits
{
public const int FirstTurnMs = 1000;
public const int TurnMs = 100;
public const int SafetyMarginMs = 15;
}
}

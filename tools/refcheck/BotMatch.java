import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.File;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

import com.codingame.game.engine.Action;
import com.codingame.game.engine.Constants;
import com.codingame.game.engine.DraftPhase;
import com.codingame.game.engine.GameState;
import com.codingame.game.engine.InvalidActionHard;
import com.codingame.game.engine.RefereeParams;

/**
 * Мини-арбитр: настоящий движок арбитра + два агента. Агент — либо внешний процесс (строка команды,
 * протокол stdin/stdout как на CodinGame), либо встроенный "random". Стороны чередуются по партиям.
 * Время хода замеряется (лимит 1000 мс на первые ходы, 100 мс на остальные), но процесс не убивается:
 * превышения считаются и печатаются; ответ дольше лимита + 3 с — поражение.
 *
 * Запуск: java BotMatch <игр> <seed> "<cmdA>" "<cmdB>" [каталог логов]
 */
public class BotMatch {
    static final int GRACE_MS = 3000;

    static final class Stats {
        int wins, games, turns, overLimit, illegal, crashes;
        long maxMs, totalMs;
        void addTurn(long ms, long limit) { turns++; totalMs += ms; if (ms > maxMs) maxMs = ms; if (ms > limit) overLimit++; }
        public String toString() {
            return String.format("wins %d/%d, turns %d, avg %.1f ms, max %d ms, over limit %d, illegal actions %d, crashes/timeouts %d",
                    wins, games, turns, turns == 0 ? 0.0 : (double) totalMs / turns, maxMs, overLimit, illegal, crashes);
        }
    }

    static final class ProcessAgent implements AutoCloseable {
        final Process proc;
        final BufferedWriter in;
        final BlockingQueue<String> out = new LinkedBlockingQueue<>();

        ProcessAgent(String cmd, File errFile) throws IOException {
            ProcessBuilder pb = new ProcessBuilder("bash", "-c", cmd);
            pb.redirectError(errFile != null ? ProcessBuilder.Redirect.to(errFile) : ProcessBuilder.Redirect.DISCARD);
            proc = pb.start();
            in = new BufferedWriter(new OutputStreamWriter(proc.getOutputStream(), StandardCharsets.UTF_8));
            Thread reader = new Thread(() -> {
                try (BufferedReader r = new BufferedReader(new InputStreamReader(proc.getInputStream(), StandardCharsets.UTF_8))) {
                    String line;
                    while ((line = r.readLine()) != null) out.add(line);
                } catch (IOException ignored) {
                }
            });
            reader.setDaemon(true);
            reader.start();
        }

        /** Отправляет ввод, ждёт одну строку ответа. null — таймаут или падение. */
        String turn(List<String> lines, long limitMs, Stats stats) throws IOException {
            for (String l : lines) {
                in.write(l);
                in.write('\n');
            }
            in.flush();
            long t0 = System.nanoTime();
            String ans;
            try {
                ans = out.poll(limitMs + GRACE_MS, TimeUnit.MILLISECONDS);
            } catch (InterruptedException e) {
                ans = null;
            }
            long ms = (System.nanoTime() - t0) / 1_000_000;
            if (ans == null) {
                stats.crashes++;
                return null;
            }
            stats.addTurn(ms, limitMs);
            return ans;
        }

        public void close() { proc.destroyForcibly(); }
    }

    public static void main(String[] args) throws Exception {
        int games = Integer.parseInt(args[0]);
        long seed = Long.parseLong(args[1]);
        String[] cmds = { args[2], args[3] };
        File logDir = args.length > 4 ? new File(args[4]) : null;
        if (logDir != null) logDir.mkdirs();

        Constants.VERBOSE_LEVEL = 0;
        Constants.LoadCardlist("cardlist.txt");
        Random rng = new Random(seed);
        Stats[] stats = { new Stats(), new Stats() };

        for (int g = 0; g < games; g++) {
            int[] side = { g % 2, 1 - g % 2 };   // side[bot] = player index в этой партии
            String[] perPlayer = { cmds[side[0] == 0 ? 0 : 1], cmds[side[0] == 1 ? 0 : 1] };
            Stats[] perPlayerStats = { stats[side[0] == 0 ? 0 : 1], stats[side[0] == 1 ? 0 : 1] };
            long gs = rng.nextLong();
            int winner = playGame(gs, perPlayer, perPlayerStats, logDir);
            int winnerBot = side[0] == winner ? 0 : 1;
            stats[winnerBot].wins++;
            stats[0].games++;
            stats[1].games++;
            System.out.println("game " + (g + 1) + " seed " + gs + ": bot " + (winnerBot == 0 ? "A" : "B") + " (player " + winner + ") won");
        }
        System.out.println("A: " + stats[0]);
        System.out.println("B: " + stats[1]);
    }

    /** Возвращает индекс победителя (0/1). */
    static int playGame(long seed, String[] cmds, Stats[] stats, File logDir) throws Exception {
        Random rng = new Random(seed);
        RefereeParams params = new RefereeParams(rng.nextLong(), rng.nextLong(), rng.nextLong());
        DraftPhase draft = new DraftPhase(DraftPhase.Difficulty.NORMAL, params);
        draft.PrepareChoices();

        ProcessAgent[] agents = new ProcessAgent[2];
        StringBuilder[] logs = { new StringBuilder(), new StringBuilder() };
        int loser = -1;
        try {
            for (int p = 0; p < 2; p++)
                if (!cmds[p].equals("random"))
                    agents[p] = new ProcessAgent(cmds[p], logDir == null ? null : new File(logDir, "game-" + seed + "-p" + p + ".err"));

            // драфт
            for (int t = 0; t < Constants.CARDS_IN_DECK && loser < 0; t++) {
                for (int p = 0; p < 2 && loser < 0; p++) {
                    List<String> lines = new ArrayList<>();
                    for (String line : draft.getMockPlayersInput(p, t)) lines.add(line);
                    for (int c = 0; c < 3; c++) lines.add(draft.draft[t][c].getAsInput());
                    String ans;
                    if (agents[p] != null) {
                        ans = agents[p].turn(lines, t == 0 ? Constants.TIMELIMIT_FIRSTDRAFTTURN : Constants.TIMELIMIT_DRAFTTURN, stats[p]);
                        if (ans == null) { loser = p; break; }
                    } else {
                        ans = "PICK " + rng.nextInt(3);
                    }
                    try {
                        draft.PlayerChoice(t, ans, p);
                    } catch (InvalidActionHard e) {
                        loser = p;
                    }
                    for (String l : lines) logs[p].append(l).append('\n');
                    logs[p].append("> ").append(ans).append('\n');
                }
            }
            if (loser >= 0) return 1 - loser;

            draft.ShuffleDecks();
            GameState state = new GameState(draft);
            int battleTurn = 0;
            while (state.winner == -1) {
                state.AdvanceState();
                int p = state.currentPlayer;
                List<String> lines = new ArrayList<>();
                for (String line : state.getPlayersInput()) lines.add(line);
                for (String line : state.getCardsInput()) lines.add(line);
                for (String l : lines) logs[p].append(l).append('\n');

                if (state.winner != -1) { // погиб на своём доборе: ввод всё равно уходит, ответ не важен
                    if (agents[p] != null) agents[p].turn(lines, Constants.TIMELIMIT_GAMETURN, stats[p]);
                    logs[p].append("> PASS\n");
                    break;
                }

                long limit = battleTurn < 2 ? Constants.TIMELIMIT_FIRSTGAMETURN : Constants.TIMELIMIT_GAMETURN;
                battleTurn++;
                if (agents[p] != null) {
                    String ans = agents[p].turn(lines, limit, stats[p]);
                    if (ans == null) { loser = p; break; }
                    logs[p].append("> ").append(ans).append('\n');
                    List<Action> actions;
                    try {
                        actions = Action.parseSequence(ans);
                    } catch (InvalidActionHard e) {
                        loser = p;
                        break;
                    }
                    while (!actions.isEmpty() && state.winner == -1) {
                        Action a = actions.remove(0);
                        if (a.type == Action.Type.PASS) continue;
                        if (!state.computeLegalActions().contains(a)) { stats[p].illegal++; continue; }
                        state.AdvanceState(a);
                    }
                } else {
                    List<String> answer = new ArrayList<>();
                    while (state.winner == -1) {
                        List<Action> legals = state.computeLegalActions();
                        if (legals.size() == 1 || rng.nextDouble() > 0.85) break;
                        Action a = legals.get(rng.nextInt(legals.size() - 1));
                        answer.add(a.toStringNoText());
                        state.AdvanceState(a);
                    }
                    logs[p].append("> ").append(answer.isEmpty() ? "PASS" : String.join(";", answer)).append('\n');
                }
            }
            if (loser >= 0) return 1 - loser;
            return state.winner;
        } finally {
            for (ProcessAgent a : agents) if (a != null) a.close();
            if (logDir != null)
                for (int p = 0; p < 2; p++)
                    Files.write(new File(logDir, "game-" + seed + "-p" + p + ".log").toPath(),
                            ("# seed=" + seed + " player=" + p + " bot=" + cmds[p] + "\n" + logs[p]).getBytes(StandardCharsets.UTF_8));
        }
    }
}

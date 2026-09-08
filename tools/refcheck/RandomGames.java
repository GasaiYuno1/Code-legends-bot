import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import com.codingame.game.engine.Action;
import com.codingame.game.engine.Constants;
import com.codingame.game.engine.DraftPhase;
import com.codingame.game.engine.GameState;
import com.codingame.game.engine.InvalidActionHard;
import com.codingame.game.engine.RefereeParams;

/**
 * Случайные партии на настоящем движке арбитра (классы engine/* без SDK).
 * На каждый ход каждого игрока пишет ввод ровно в формате арбитра и строку "> ответ",
 * причём в ответ намеренно подмешиваются нелегальные действия и PASS посреди хода —
 * арбитр их пропускает, симулятор должен делать то же.
 *
 * Запуск: java RandomGames <seed> <игр> <каталог> [NORMAL|LESS_EASY|EASY|VERY_EASY]
 * Вывод: <каталог>/game-<seed>-p0.log и -p1.log (перспектива каждого игрока).
 */
public class RandomGames {
    public static void main(String[] args) throws Exception {
        long seed = Long.parseLong(args[0]);
        int games = Integer.parseInt(args[1]);
        File outDir = new File(args[2]);
        DraftPhase.Difficulty difficulty = args.length > 3 ? DraftPhase.Difficulty.valueOf(args[3]) : DraftPhase.Difficulty.NORMAL;
        outDir.mkdirs();

        Constants.VERBOSE_LEVEL = 0;
        Constants.LoadCardlist("cardlist.txt");
        Random rng = new Random(seed);
        for (int g = 0; g < games; g++)
            playGame(rng.nextLong(), outDir, difficulty);
    }

    static void playGame(long seed, File outDir, DraftPhase.Difficulty difficulty) throws IOException, InvalidActionHard {
        Random rng = new Random(seed);
        // Доля ходов с действием: маленькая → длинные партии (пустая колода, лимит 50 ходов), большая → быстрые размены.
        double[] buckets = { 0.03, 0.3, 0.6, 0.85, 0.95 };
        double act = buckets[rng.nextInt(buckets.length)];

        RefereeParams params = new RefereeParams(rng.nextLong(), rng.nextLong(), rng.nextLong());
        DraftPhase draft = new DraftPhase(difficulty, params);
        draft.PrepareChoices();

        StringBuilder[] logs = { new StringBuilder(), new StringBuilder() };
        for (int t = 0; t < Constants.CARDS_IN_DECK; t++) {
            for (int p = 0; p < 2; p++) {
                for (String line : draft.getMockPlayersInput(p, t)) logs[p].append(line).append('\n');
                for (int c = 0; c < 3; c++) logs[p].append(draft.draft[t][c].getAsInput()).append('\n');
                int pick = rng.nextInt(3);
                draft.PlayerChoice(t, "PICK " + pick, p);
                logs[p].append("> PICK ").append(pick).append('\n');
            }
        }
        draft.ShuffleDecks();
        GameState state = new GameState(draft);

        int turns = 0;
        while (state.winner == -1 && turns < 400) {
            state.AdvanceState();
            int p = state.currentPlayer;
            for (String line : state.getPlayersInput()) logs[p].append(line).append('\n');
            for (String line : state.getCardsInput()) logs[p].append(line).append('\n');
            turns++;

            List<String> answer = new ArrayList<>();
            int steps = 0;
            while (state.winner == -1 && steps < 100) {
                List<Action> legals = state.computeLegalActions();
                if (rng.nextInt(6) == 0) {
                    Action bogus = randomBogus(rng, legals);
                    if (bogus != null) answer.add(bogus.toStringNoText());
                }
                if (rng.nextInt(10) == 0) answer.add("PASS");
                if (legals.size() == 1 || rng.nextDouble() > act) break; // только PASS или решили закончить ход
                Action a = legals.get(rng.nextInt(legals.size() - 1)); // последний — PASS
                answer.add(a.toStringNoText());
                state.AdvanceState(a);
                steps++;
            }
            logs[p].append("> ").append(answer.isEmpty() ? "PASS" : String.join(";", answer)).append('\n');
        }

        for (int p = 0; p < 2; p++) {
            String header = "# seed=" + seed + " difficulty=" + difficulty + " act=" + act + " player=" + p
                    + " turns=" + turns + " winner=" + state.winner + "\n";
            File f = new File(outDir, "game-" + seed + "-p" + p + ".log");
            Files.write(f.toPath(), (header + logs[p]).getBytes(StandardCharsets.UTF_8));
        }
        System.out.println("game " + seed + ": act=" + act + " turns=" + turns + " winner=" + state.winner);
    }

    /** Случайное действие, которого нет среди легальных (по equals арбитра). */
    static Action randomBogus(Random rng, List<Action> legals) {
        for (int tries = 0; tries < 10; tries++) {
            int id = rng.nextInt(62) - 1;
            int target = rng.nextInt(62) - 1;
            Action a;
            switch (rng.nextInt(3)) {
                case 0: a = Action.newSummon(id); break;
                case 1: a = Action.newAttack(id, target); break;
                default: a = Action.newUse(id, target); break;
            }
            if (!legals.contains(a)) return a;
        }
        return null;
    }
}

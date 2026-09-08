import java.io.BufferedReader;
import java.io.File;
import java.io.FileReader;
import java.io.IOException;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

import com.codingame.game.engine.Action;
import com.codingame.game.engine.Card;
import com.codingame.game.engine.Constants;
import com.codingame.game.engine.DraftPhase;
import com.codingame.game.engine.GameState;
import com.codingame.game.engine.RefereeParams;

/**
 * Воспроизводит партии арены (data/arena/games.txt от tools/arena/fetch.py) настоящим движком арбитра:
 * по сидам восстанавливает драфт и колоды, применяет пики и действия игроков и проверяет, что всё легально
 * и победитель совпадает. Пишет логи ходов в формате replay (ввод + "> ответ") для каждого игрока
 * и таблицу драфта: gameId, player, pick, offered×3, chosen, won, league.
 *
 * Запуск: java ArenaReplay <games.txt> <каталог логов> <draft.tsv>
 */
public class ArenaReplay {
    static final class Game {
        long id;
        int winner;
        long[] agents = new long[2];
        int[] leagues = new int[2];
        String[] names = new String[2];
        long[] seeds = new long[3];
        int[][] picks = { new int[30], new int[30] };
        List<int[]> turnPlayers = new ArrayList<>();
        List<String> turnActions = new ArrayList<>();
    }

    public static void main(String[] args) throws Exception {
        File games = new File(args[0]);
        File logDir = new File(args[1]);
        File draftTsv = new File(args[2]);
        logDir.mkdirs();
        Constants.VERBOSE_LEVEL = 0;
        Constants.LoadCardlist("cardlist.txt");

        int ok = 0, failed = 0, turnsTotal = 0;
        try (PrintWriter tsv = new PrintWriter(draftTsv, "UTF-8")) {
            tsv.println("gameId\tplayer\tpick\toffered1\toffered2\toffered3\tchosen\twon\tleague");
            for (Game g : read(games)) {
                String err = replay(g, logDir, tsv);
                if (err == null) {
                    ok++;
                    turnsTotal += g.turnActions.size();
                } else {
                    failed++;
                    System.out.println("game " + g.id + ": " + err);
                }
            }
        }
        System.out.println(ok + " games replayed (" + turnsTotal + " turns), " + failed + " failed");
    }

    static List<Game> read(File file) throws IOException {
        List<Game> list = new ArrayList<>();
        Game g = null;
        try (BufferedReader r = new BufferedReader(new FileReader(file, StandardCharsets.UTF_8))) {
            String line;
            while ((line = r.readLine()) != null) {
                line = line.trim();
                if (line.isEmpty()) continue;
                String[] p = line.split("\\s+");
                switch (p[0]) {
                    case "game":
                        g = new Game();
                        list.add(g);
                        g.id = Long.parseLong(p[1]);
                        g.winner = Integer.parseInt(p[3]);
                        g.agents[0] = Long.parseLong(p[5]);
                        g.agents[1] = Long.parseLong(p[6]);
                        g.leagues[0] = Integer.parseInt(p[8]);
                        g.leagues[1] = Integer.parseInt(p[9]);
                        g.names[0] = p[11];
                        g.names[1] = p[12];
                        break;
                    case "seeds":
                        for (int i = 0; i < 3; i++) g.seeds[i] = Long.parseLong(p[1 + i]);
                        break;
                    case "picks0":
                    case "picks1":
                        int pl = p[0].equals("picks0") ? 0 : 1;
                        for (int i = 0; i < 30; i++) g.picks[pl][i] = Integer.parseInt(p[1 + i]);
                        break;
                    case "turn":
                        g.turnPlayers.add(new int[] { Integer.parseInt(p[1]) });
                        g.turnActions.add(line.substring(line.indexOf(' ', 5) + 1));
                        break;
                    default:
                        break;
                }
            }
        }
        return list;
    }

    /** null — партия воспроизведена без расхождений; иначе описание проблемы. */
    static String replay(Game g, File logDir, PrintWriter tsv) throws Exception {
        RefereeParams params = new RefereeParams(g.seeds[0], g.seeds[1], g.seeds[2]);
        DraftPhase draft = new DraftPhase(DraftPhase.Difficulty.NORMAL, params);
        draft.PrepareChoices();

        StringBuilder[] logs = { new StringBuilder(), new StringBuilder() };
        for (int p = 0; p < 2; p++)
            logs[p].append("# arena game ").append(g.id).append(" player ").append(p).append(" agent ").append(g.agents[p])
                    .append(" name ").append(g.names[p]).append(" league ").append(g.leagues[p])
                    .append(" winner ").append(g.winner).append('\n');

        for (int t = 0; t < Constants.CARDS_IN_DECK; t++) {
            Card[] offered = draft.draft[t];
            for (int p = 0; p < 2; p++) {
                for (String line : draft.getMockPlayersInput(p, t)) logs[p].append(line).append('\n');
                for (int c = 0; c < 3; c++) logs[p].append(offered[c].getAsInput()).append('\n');
                int pick = g.picks[p][t];
                int idx = -1;
                for (int c = 0; c < 3; c++) if (offered[c].baseId == pick) { idx = c; break; }
                if (idx < 0)
                    return "pick " + t + " player " + p + ": card " + pick + " not among offered "
                            + offered[0].baseId + " " + offered[1].baseId + " " + offered[2].baseId + " (wrong seeds or pool)";
                draft.PlayerChoice(t, "PICK " + idx, p);
                logs[p].append("> PICK ").append(idx).append('\n');
                tsv.println(g.id + "\t" + p + "\t" + t + "\t" + offered[0].baseId + "\t" + offered[1].baseId + "\t" + offered[2].baseId
                        + "\t" + pick + "\t" + (g.winner == p ? 1 : 0) + "\t" + g.leagues[p]);
            }
        }
        draft.ShuffleDecks();
        GameState state = new GameState(draft);

        for (int i = 0; i < g.turnActions.size(); i++) {
            int p = g.turnPlayers.get(i)[0];
            if (state.winner != -1) return "turn " + i + ": game already over (winner " + state.winner + ")";
            state.AdvanceState();
            if (state.currentPlayer != p) return "turn " + i + ": expected player " + p + " but engine has " + state.currentPlayer;
            for (String line : state.getPlayersInput()) logs[p].append(line).append('\n');
            for (String line : state.getCardsInput()) logs[p].append(line).append('\n');
            String answer = g.turnActions.get(i);
            logs[p].append("> ").append(answer).append('\n');
            if (state.winner != -1) continue;   // погиб на доборе: ввод получил, действий нет
            for (Action a : Action.parseSequence(answer)) {
                if (a.type == Action.Type.PASS) continue;
                if (state.winner != -1) return "turn " + i + ": action " + a.toStringNoText() + " after game end";
                if (!state.computeLegalActions().contains(a))
                    return "turn " + i + ": action " + a.toStringNoText() + " is illegal in engine";
                state.AdvanceState(a);
            }
        }
        if (state.winner == -1) {
            // партия могла закончиться таймаутом/ошибкой соперника: победитель по данным арены, состояние не терминальное
            for (int p = 0; p < 2; p++) logs[p].append("# note: engine state not terminal at end of log\n");
        } else if (state.winner != g.winner) {
            return "winner mismatch: engine " + state.winner + ", arena " + g.winner;
        }
        for (int p = 0; p < 2; p++)
            Files.write(new File(logDir, "arena-" + g.id + "-p" + p + ".log").toPath(), logs[p].toString().getBytes(StandardCharsets.UTF_8));
        return null;
    }
}

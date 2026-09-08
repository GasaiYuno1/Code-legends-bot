package com.codingame.gameengine.core;

import java.util.Properties;

/** Заглушка SDK CodinGame: RefereeParams читает отсюда seed и параметры. */
public class MultiplayerGameManager<T> {
    public Long getSeed() { return 0L; }
    public Properties getGameParameters() { return new Properties(); }
    public int getLeagueLevel() { return 4; }
}

create table DSP(
    Product VARCHAR(40) NOT NULL,
    Planet VARCHAR(40) NOT NULL,
    Star VARCHAR(40),
    production_actual INT(11),
    consumption_actual INT(11),
    production_theoretical INT(11),
    consumption_theoretical INT(11),
    producers INT(11),
    consumers INT(11),
    game_time_elapsed BIGINT(20) NOT NULL,
    unique_game_identifier VARCHAR(40),
    event_time DATETIME DEFAULT NOW(),
    Product_Planet_elapsed VARCHAR(120) NOT NULL
);
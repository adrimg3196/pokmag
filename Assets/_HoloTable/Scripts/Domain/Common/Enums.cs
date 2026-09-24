namespace HoloTable.Domain
{
    /// <summary>Tabletop game a physical card or miniature belongs to.</summary>
    public enum GameSystem
    {
        Pokemon = 0,
        MagicTheGathering = 1,
        Warhammer = 2,
    }

    /// <summary>Which player controls an entity. Resolved from its physical side of the table.</summary>
    public enum PlayerSide
    {
        Neutral = 0,
        PlayerOne = 1,
        PlayerTwo = 2,
    }

    /// <summary>
    /// Shared elemental palette. Pokémon types map 1:1; MTG colours and Warhammer
    /// weapon flavours reuse it to pick VFX (Fire = red mana / flamers, etc.).
    /// </summary>
    public enum ElementType
    {
        None = 0,
        Colorless,
        Fire,
        Water,
        Lightning,
        Grass,
        Psychic,
        Fighting,
        Darkness,
        Metal,
        Dragon,
        Fairy,
    }

    /// <summary>Physical presence on the table. Drives the dynamic scale rules.</summary>
    public enum SizeClass
    {
        Tiny = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
        Huge = 4,
        Colossal = 5,
    }

    public static class PlayerSideExtensions
    {
        /// <summary>True when both sides belong to real, different players.</summary>
        public static bool IsRivalOf(this PlayerSide self, PlayerSide other) =>
            self != PlayerSide.Neutral && other != PlayerSide.Neutral && self != other;

        public static PlayerSide Opponent(this PlayerSide self) => self switch
        {
            PlayerSide.PlayerOne => PlayerSide.PlayerTwo,
            PlayerSide.PlayerTwo => PlayerSide.PlayerOne,
            _ => PlayerSide.Neutral,
        };
    }
}

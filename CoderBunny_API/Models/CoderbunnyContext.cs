using CoderBunny_API1_Updated.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class CoderbunnyContext : DbContext
{
    public CoderbunnyContext()
    {
    }

    public CoderbunnyContext(DbContextOptions<CoderbunnyContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BoardConfig> BoardConfigs { get; set; }

    public virtual DbSet<CardMaster> CardMasters { get; set; }

    public virtual DbSet<Game> Games { get; set; }

    public virtual DbSet<GameMove> GameMoves { get; set; }

    public virtual DbSet<GamePlayers> GamePlayers { get; set; }

    public virtual DbSet<GameTurn> GameTurns { get; set; }

    public virtual DbSet<Player> Players { get; set; }

    public virtual DbSet<PlayerCard> PlayerCards { get; set; }

    public virtual DbSet<PlayerCardUsage> PlayerCardUsages { get; set; }

    public virtual DbSet<PlayerFunctionCards> PlayerFunctionCards { get; set; }

    public virtual DbSet<Result> Results { get; set; }

    // ✅ GameStats table
    public virtual DbSet<GameStats> GameStats { get; set; }

    // ✅ NEW — HostUser table
    public virtual DbSet<HostUser> HostUser { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=DESKTOP-R7IOMMK;Database=coderbunny;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BoardConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__BoardCon__3214EC07FFA3BAAB");

            entity.ToTable("BoardConfig");

            entity.Property(e => e.AssetType)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<CardMaster>(entity =>
        {
            entity.HasKey(e => e.CardId).HasName("PK__CardMast__55FECDAE1A09A6C0");

            entity.ToTable("CardMaster");

            entity.Property(e => e.CardName)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.GameId).HasName("PK__Game__2AB897FDE90083C9");

            entity.ToTable("Game");

            entity.Property(e => e.DifficultyLevel)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.GameStatus)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.RoomCode).HasMaxLength(6);

            // ✅ timing columns for stats calculation
            entity.Property(e => e.StartedAt).HasColumnType("datetime");
            entity.Property(e => e.CompletedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<GameMove>(entity =>
        {
            entity.HasKey(e => e.MoveId).HasName("PK__GameMove__A931A41CD87F2C1C");

            entity.ToTable("GameMove");

            entity.Property(e => e.MoveTime)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Game).WithMany(p => p.GameMoves)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GameMove_Game");

            entity.HasOne(d => d.Player).WithMany(p => p.GameMoves)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GameMove_Player");
        });

        modelBuilder.Entity<GamePlayers>(entity =>
        {
            entity.HasKey(e => e.GamePlayerId).HasName("PK__GamePlay__2D47DF8EDC18E736");

            entity.Property(e => e.CurrentPosition).HasDefaultValue(0);
            entity.Property(e => e.Direction).HasMaxLength(20);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Game).WithMany(p => p.GamePlayers)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__GamePlaye__GameI__04E4BC85");

            entity.HasOne(d => d.Player).WithMany(p => p.GamePlayers)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__GamePlaye__Playe__05D8E0BE");
        });

        modelBuilder.Entity<GameTurn>(entity =>
        {
            entity.HasKey(e => e.GameTurnId).HasName("PK__GameTurn__CF8E7A86BC3534FA");

            entity.ToTable("GameTurn");

            entity.HasOne(d => d.CurrentPlayer).WithMany(p => p.GameTurns)
                .HasForeignKey(d => d.CurrentPlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GameTurn_Player");

            entity.HasOne(d => d.Game).WithMany(p => p.GameTurns)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GameTurn_Game");
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.PlayerId).HasName("PK__Player__4A4E74C89B5C4F05");

            entity.ToTable("Player");

            entity.Property(e => e.PlayerImage)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.PlayerName)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.Game).WithMany(p => p.Players)
                .HasForeignKey(d => d.GameId)
                .HasConstraintName("FK_Player_Game");
        });

        modelBuilder.Entity<PlayerCard>(entity =>
        {
            entity.HasKey(e => e.PlayerCardId).HasName("PK__PlayerCa__3C5B0183D653D289");

            entity.ToTable("PlayerCard");

            entity.HasIndex(e => new { e.PlayerId, e.CardId, e.GameId }, "UQ_PlayerCard").IsUnique();

            entity.Property(e => e.GameId).HasDefaultValue(0);

            entity.HasOne(d => d.Card).WithMany(p => p.PlayerCards)
                .HasForeignKey(d => d.CardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PlayerCard_Card");

            entity.HasOne(d => d.Game).WithMany(p => p.PlayerCards)
                .HasForeignKey(d => d.GameId)
                .HasConstraintName("FK_PlayerCard_Game");

            entity.HasOne(d => d.Player).WithMany(p => p.PlayerCards)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PlayerCard_Player");
        });

        modelBuilder.Entity<PlayerCardUsage>(entity =>
        {
            entity.HasKey(e => e.UsageId).HasName("PK__PlayerCa__29B19720A7182C2E");

            entity.ToTable("PlayerCardUsage");

            entity.Property(e => e.IsFunction).HasDefaultValue(false);
            entity.Property(e => e.UsedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Card).WithMany(p => p.PlayerCardUsages)
                .HasForeignKey(d => d.CardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Usage_Card");

            entity.HasOne(d => d.Game).WithMany(p => p.PlayerCardUsages)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Usage_Game");

            entity.HasOne(d => d.Move).WithMany(p => p.PlayerCardUsages)
                .HasForeignKey(d => d.MoveId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Usage_Move");

            entity.HasOne(d => d.Player).WithMany(p => p.PlayerCardUsages)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Usage_Player");
        });

        modelBuilder.Entity<PlayerFunctionCards>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__PlayerFu__3214EC07FB3DD053");

            entity.HasIndex(e => new { e.GameId, e.PlayerId, e.OrderNo }, "UQ_Function").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.LastUsedAt).HasColumnType("datetime");
            entity.Property(e => e.MaxUsage).HasDefaultValue(3);
            entity.Property(e => e.UsageCount).HasDefaultValue(0);

            entity.HasOne(d => d.Card).WithMany(p => p.PlayerFunctionCards)
                .HasForeignKey(d => d.CardId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PlayerFun__CardI__540C7B00");

            entity.HasOne(d => d.Game).WithMany(p => p.PlayerFunctionCards)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PlayerFun__GameI__5224328E");

            entity.HasOne(d => d.Player).WithMany(p => p.PlayerFunctionCards)
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PlayerFun__Playe__531856C7");
        });

        modelBuilder.Entity<Result>(entity =>
        {
            entity.HasKey(e => e.ResultId).HasName("PK__Result__97690208E902DE11");

            entity.ToTable("Result");

            entity.Property(e => e.Remarks)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.Game).WithMany(p => p.Results)
                .HasForeignKey(d => d.GameId)
                .HasConstraintName("FK_Result_Game");

            entity.HasOne(d => d.Player).WithMany(p => p.Results)
                .HasForeignKey(d => d.PlayerId)
                .HasConstraintName("FK_Result_Player");
        });

        // ✅ GameStats entity configuration
        modelBuilder.Entity<GameStats>(entity =>
        {
            entity.HasKey(e => e.StatId).HasName("PK__GameStat__B6A5B09D");

            entity.ToTable("GameStats");

            entity.HasIndex(e => new { e.GameId, e.PlayerId }, "UQ_GameStats_GamePlayer").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.Property(e => e.TotalMoves).HasDefaultValue(0);
            entity.Property(e => e.OptimalMoves).HasDefaultValue(0);
            entity.Property(e => e.LoopUsedCount).HasDefaultValue(0);
            entity.Property(e => e.FunctionUsedCount).HasDefaultValue(0);
            entity.Property(e => e.JumpUsedCount).HasDefaultValue(0);
            entity.Property(e => e.ForwardUsedCount).HasDefaultValue(0);
            entity.Property(e => e.TurnUsedCount).HasDefaultValue(0);
            entity.Property(e => e.BugUsedCount).HasDefaultValue(0);
            entity.Property(e => e.EfficiencyScore).HasDefaultValue(0);
            entity.Property(e => e.SpeedScore).HasDefaultValue(0);
            entity.Property(e => e.LogicScore).HasDefaultValue(0);

            entity.HasOne(d => d.Game)
                .WithMany(p => p.GameStats)
                .HasForeignKey(d => d.GameId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GameStats_Game");

            entity.HasOne(d => d.Player)
                .WithMany()
                .HasForeignKey(d => d.PlayerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GameStats_Player");
        });

        // ✅ NEW — HostUser entity configuration
        modelBuilder.Entity<HostUser>(entity =>
        {
            entity.HasKey(e => e.HostUserId).HasName("PK__HostUser__HostUserId");

            entity.ToTable("HostUser");

            entity.HasIndex(e => e.Username).IsUnique();

            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false);

            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
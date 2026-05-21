using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Library.Vtf;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Services.Library.Vtf
{
    /// <summary>
    /// Loss-proof round-trip coverage for <see cref="VtfPackager"/>. Every
    /// persisted field on every snapshot DTO that travels through Pack/Unpack
    /// gets at least one assertion that exercises a non-default value; the
    /// `RoundTrip_FullSlot_JsonGraph_IsLossless` test then closes the loop by
    /// JSON-comparing the whole graph so a forgotten field surfaces as a
    /// failure rather than silent data loss.
    /// </summary>
    [TestClass]
    public class VtfPackagerTests
    {
        // ── LibraryCoreSnapshot.Settings (DndMapperSettings) ──────────────────

        [TestMethod]
        public void RoundTrip_AllSettingsFields_Survive()
        {
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    Settings = new DndMapperSettings
                    {
                        TokenMovement = TokenMovementPolicy.HostOnly,
                        SheetEditByOthers = SheetEditPolicy.Anyone,
                        RollsVisibleToPlayers = false,
                        PlayersCanCreateNPCs = true,
                        HpTrackingEnabled = false,
                        PlayersCanSeeOtherSheets = true,
                    },
                },
            };

            var output = RoundTrip(input);

            Assert.AreEqual(TokenMovementPolicy.HostOnly, output.Core.Settings.TokenMovement);
            Assert.AreEqual(SheetEditPolicy.Anyone, output.Core.Settings.SheetEditByOthers);
            Assert.IsFalse(output.Core.Settings.RollsVisibleToPlayers);
            Assert.IsTrue(output.Core.Settings.PlayersCanCreateNPCs);
            Assert.IsFalse(output.Core.Settings.HpTrackingEnabled);
            Assert.IsTrue(output.Core.Settings.PlayersCanSeeOtherSheets);
        }

        // ── LibraryCoreSnapshot scalars ───────────────────────────────────────

        [TestMethod]
        public void RoundTrip_CoreScalars_PreserveActiveSchemaAndInitiativeAttribute()
        {
            var schemaId = Guid.NewGuid();
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    ActiveSchemaTemplateId = schemaId,
                    InitiativeAttributeName = "INT",
                },
            };

            var output = RoundTrip(input);

            Assert.AreEqual(schemaId, output.Core.ActiveSchemaTemplateId);
            Assert.AreEqual("INT", output.Core.InitiativeAttributeName);
        }

        [TestMethod]
        public void RoundTrip_CoreScalars_PreserveNullActiveSchemaAndInitiativeAttribute()
        {
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    ActiveSchemaTemplateId = null,
                    InitiativeAttributeName = null,
                },
            };

            var output = RoundTrip(input);

            Assert.IsNull(output.Core.ActiveSchemaTemplateId);
            Assert.IsNull(output.Core.InitiativeAttributeName);
        }

        // ── LibraryCoreSnapshot.AttributeSchema ───────────────────────────────

        [TestMethod]
        public void RoundTrip_AttributeSchema_CustomPresetRoundTripsEveryValueType()
        {
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    AttributeSchema = new AttributeSchemaSnapshot
                    {
                        Preset = AttributePreset.Custom,
                        Rows =
                        [
                            new AttributeRowSnapshot
                            {
                                Name = "STR",
                                Type = AttributeValueType.Score,
                                Default = new AttributeValueSnapshot { Type = AttributeValueType.Score, IntValue = 14 },
                            },
                            new AttributeRowSnapshot
                            {
                                Name = "Athletics",
                                Type = AttributeValueType.Modifier,
                                Default = new AttributeValueSnapshot { Type = AttributeValueType.Modifier, IntValue = -2 },
                            },
                            new AttributeRowSnapshot
                            {
                                Name = "Class",
                                Type = AttributeValueType.Text,
                                Default = new AttributeValueSnapshot { Type = AttributeValueType.Text, StringValue = "Warlock" },
                            },
                        ],
                    },
                },
            };

            var output = RoundTrip(input);

            Assert.AreEqual(AttributePreset.Custom, output.Core.AttributeSchema.Preset);
            Assert.HasCount(3, output.Core.AttributeSchema.Rows);
            var byName = output.Core.AttributeSchema.Rows.ToDictionary(r => r.Name);
            Assert.AreEqual(AttributeValueType.Score, byName["STR"].Type);
            Assert.AreEqual(14, byName["STR"].Default.IntValue);
            Assert.AreEqual(AttributeValueType.Modifier, byName["Athletics"].Type);
            Assert.AreEqual(-2, byName["Athletics"].Default.IntValue);
            Assert.AreEqual(AttributeValueType.Text, byName["Class"].Type);
            Assert.AreEqual("Warlock", byName["Class"].Default.StringValue);
        }

        [TestMethod]
        public void RoundTrip_AttributeSchema_BuiltInPresetRoundTrips()
        {
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    AttributeSchema = new AttributeSchemaSnapshot
                    {
                        Preset = AttributePreset.SimpleD20,
                        Rows = [], // built-in presets re-seed Rows on load
                    },
                },
            };

            var output = RoundTrip(input);

            Assert.AreEqual(AttributePreset.SimpleD20, output.Core.AttributeSchema.Preset);
        }

        // ── LibraryCoreSnapshot.CustomTemplates ───────────────────────────────

        [TestMethod]
        public void RoundTrip_CustomTemplates_PreserveFullFidelity()
        {
            var templateId = Guid.NewGuid();
            var statusEffectId = Guid.NewGuid();
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    CustomTemplates =
                    [
                        new NamedTemplateSnapshot
                        {
                            Id = templateId,
                            Name = "House rules",
                            IsBuiltIn = false,
                            InitiativeAttributeName = "WIS",
                            Rows =
                            [
                                new AttributeRowSnapshot
                                {
                                    Name = "WIS",
                                    Type = AttributeValueType.Score,
                                    Default = new AttributeValueSnapshot { Type = AttributeValueType.Score, IntValue = 12 },
                                },
                            ],
                            StatusEffectTemplates =
                            [
                                new StatusEffectTemplateSnapshot
                                {
                                    Id = statusEffectId,
                                    Name = "Blessed",
                                    MaxHpDelta = 5,
                                    OnApplyHpDelta = -1,
                                    Notes = "+5 max HP, lose 1 HP on apply",
                                    AttributeDeltas =
                                    [
                                        new AttributeDeltaSnapshot { AttributeName = "WIS", Delta = 2 },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            };

            var output = RoundTrip(input);

            Assert.HasCount(1, output.Core.CustomTemplates);
            var t = output.Core.CustomTemplates[0];
            Assert.AreEqual(templateId, t.Id);
            Assert.AreEqual("House rules", t.Name);
            Assert.IsFalse(t.IsBuiltIn);
            Assert.AreEqual("WIS", t.InitiativeAttributeName);
            Assert.HasCount(1, t.Rows);
            Assert.AreEqual("WIS", t.Rows[0].Name);
            Assert.AreEqual(12, t.Rows[0].Default.IntValue);
            Assert.HasCount(1, t.StatusEffectTemplates);
            var s = t.StatusEffectTemplates[0];
            Assert.AreEqual(statusEffectId, s.Id);
            Assert.AreEqual("Blessed", s.Name);
            Assert.AreEqual(5, s.MaxHpDelta);
            Assert.AreEqual(-1, s.OnApplyHpDelta);
            Assert.AreEqual("+5 max HP, lose 1 HP on apply", s.Notes);
            Assert.HasCount(1, s.AttributeDeltas);
            Assert.AreEqual("WIS", s.AttributeDeltas[0].AttributeName);
            Assert.AreEqual(2, s.AttributeDeltas[0].Delta);
        }

        // ── LibraryCoreSnapshot.GlobalRollTemplates ───────────────────────────

        [TestMethod]
        public void RoundTrip_GlobalRollTemplates_PreserveEveryField()
        {
            var rollId = Guid.NewGuid();
            var input = EmptySlot() with
            {
                Core = EmptyCore() with
                {
                    GlobalRollTemplates =
                    [
                        new RollTemplateSnapshot
                        {
                            Id = rollId,
                            Name = "Sneak attack",
                            Label = "+1d6 sneak",
                            Mode = RollMode.Advantage,
                            FlatModifier = 3,
                            AttributeName = "DEX",
                            Dice =
                            [
                                new DiceTermSnapshot { Count = 2, Sides = 6 },
                                new DiceTermSnapshot { Count = 1, Sides = 8 },
                            ],
                        },
                    ],
                },
            };

            var output = RoundTrip(input);

            Assert.HasCount(1, output.Core.GlobalRollTemplates);
            var r = output.Core.GlobalRollTemplates[0];
            Assert.AreEqual(rollId, r.Id);
            Assert.AreEqual("Sneak attack", r.Name);
            Assert.AreEqual("+1d6 sneak", r.Label);
            Assert.AreEqual(RollMode.Advantage, r.Mode);
            Assert.AreEqual(3, r.FlatModifier);
            Assert.AreEqual("DEX", r.AttributeName);
            Assert.HasCount(2, r.Dice);
            Assert.AreEqual(2, r.Dice[0].Count);
            Assert.AreEqual(6, r.Dice[0].Sides);
            Assert.AreEqual(1, r.Dice[1].Count);
            Assert.AreEqual(8, r.Dice[1].Sides);
        }

        // ── MapSnapshot scalars + GridConfig ──────────────────────────────────

        [TestMethod]
        public void RoundTrip_MapScalars_PreserveAllFields()
        {
            var mapId = Guid.NewGuid();
            var createdUtc = new DateTime(2026, 5, 21, 14, 30, 15, DateTimeKind.Utc);
            var map = new MapSnapshot
            {
                Id = mapId,
                Name = "Dragon's Lair",
                ListOrder = 7,
                CreatedUtc = createdUtc,
                Grid = new GridConfig
                {
                    WidthCells = 42,
                    HeightCells = 28,
                    CellPixels = 65,
                    ShowGridLines = false,
                    SnapToGrid = false,
                    LineColor = "#88aaff80",
                },
                DefaultSpawnX = 12.5,
                DefaultSpawnY = -3.25,
                FogMask = [],
            };

            var input = WithMaps(EmptySlot(), [map]);
            var output = RoundTrip(input);

            Assert.HasCount(1, output.Maps);
            var m = output.Maps[0];
            Assert.AreEqual(mapId, m.Id);
            Assert.AreEqual("Dragon's Lair", m.Name);
            Assert.AreEqual(7, m.ListOrder);
            Assert.AreEqual(createdUtc, m.CreatedUtc);
            Assert.AreEqual(DateTimeKind.Utc, m.CreatedUtc.Kind);
            Assert.AreEqual(42, m.Grid.WidthCells);
            Assert.AreEqual(28, m.Grid.HeightCells);
            Assert.AreEqual(65, m.Grid.CellPixels);
            Assert.IsFalse(m.Grid.ShowGridLines);
            Assert.IsFalse(m.Grid.SnapToGrid);
            Assert.AreEqual("#88aaff80", m.Grid.LineColor);
            Assert.AreEqual(12.5, m.DefaultSpawnX);
            Assert.AreEqual(-3.25, m.DefaultSpawnY);
        }

        [TestMethod]
        public void RoundTrip_DefaultSpawn_NullValuesSurvive()
        {
            var map = SimpleMap(Guid.NewGuid()) with
            {
                DefaultSpawnX = null,
                DefaultSpawnY = null,
            };

            var output = RoundTrip(WithMaps(EmptySlot(), [map]));

            Assert.IsNull(output.Maps[0].DefaultSpawnX);
            Assert.IsNull(output.Maps[0].DefaultSpawnY);
        }

        // ── MapSnapshot.FogMask ───────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_FullFogMask_PreservesEveryByte()
        {
            // 100×100 grid → ceil(10000/8) = 1250 bytes. Alternate-bit pattern
            // so a base64 hiccup would surface as a bit-level diff.
            var fogMask = new byte[1250];
            for (int i = 0; i < fogMask.Length; i++)
                fogMask[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

            var map = new MapSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "Fogged",
                Grid = new GridConfig { WidthCells = 100, HeightCells = 100, CellPixels = 50 },
                FogMask = fogMask,
            };

            var output = RoundTrip(WithMaps(EmptySlot(), [map]));

            CollectionAssert.AreEqual(fogMask, output.Maps[0].FogMask);
        }

        [TestMethod]
        public void RoundTrip_EmptyFogMask_StaysEmpty()
        {
            var map = SimpleMap(Guid.NewGuid()) with { FogMask = [] };

            var output = RoundTrip(WithMaps(EmptySlot(), [map]));

            Assert.HasCount(0, output.Maps[0].FogMask);
        }

        // ── MapImageSnapshot ──────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_MapImage_PreservesAllFields()
        {
            var imageId = Guid.NewGuid();
            var image = new MapImageSnapshot
            {
                Id = imageId,
                Name = "Tavern background",
                ContentType = "image/png",
                X = 2.5,
                Y = -1.25,
                Width = 17.75,
                Height = 9.125,
                OriginalWidth = 21.0,
                OriginalHeight = 10.5,
                Rotation = 47.5,
                Opacity = 0.625,
                LayerOrder = 9,
                Locked = true,
                Hidden = true,
                ByteSize = 4096,
            };
            var map = SimpleMap(Guid.NewGuid()) with { Images = [image] };
            var images = new Dictionary<Guid, VtfPackager.VtfImageAsset>
            {
                [imageId] = new VtfPackager.VtfImageAsset("image/png", PngBytes()),
            };
            var input = WithMaps(EmptySlot() with { Images = images }, [map]);

            var output = RoundTrip(input);

            Assert.HasCount(1, output.Maps[0].Images);
            var i = output.Maps[0].Images[0];
            Assert.AreEqual(imageId, i.Id);
            Assert.AreEqual("Tavern background", i.Name);
            Assert.AreEqual("image/png", i.ContentType);
            Assert.AreEqual(2.5, i.X);
            Assert.AreEqual(-1.25, i.Y);
            Assert.AreEqual(17.75, i.Width);
            Assert.AreEqual(9.125, i.Height);
            Assert.AreEqual(21.0, i.OriginalWidth);
            Assert.AreEqual(10.5, i.OriginalHeight);
            Assert.AreEqual(47.5, i.Rotation);
            Assert.AreEqual(0.625, i.Opacity);
            Assert.AreEqual(9, i.LayerOrder);
            Assert.IsTrue(i.Locked);
            Assert.IsTrue(i.Hidden);
            Assert.AreEqual(4096L, i.ByteSize);
        }

        // ── TokenSnapshot ─────────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_Token_PreservesAllFields()
        {
            var mapId = Guid.NewGuid();
            var tokenId = Guid.NewGuid();
            var sheetId = Guid.NewGuid();
            var token = new TokenSnapshot
            {
                Id = tokenId,
                Name = "Mistress Crow",
                Color = "#abcdef",
                IconKind = TokenIconKind.Solid,
                MapId = mapId,
                X = 4.5,
                Y = 6.25,
                SheetId = sheetId,
                Hidden = true,
            };
            var map = SimpleMap(mapId) with { Tokens = [token] };

            var output = RoundTrip(WithMaps(EmptySlot(), [map]));

            Assert.HasCount(1, output.Maps[0].Tokens);
            var t = output.Maps[0].Tokens[0];
            Assert.AreEqual(tokenId, t.Id);
            Assert.AreEqual("Mistress Crow", t.Name);
            Assert.AreEqual("#abcdef", t.Color);
            Assert.AreEqual(TokenIconKind.Solid, t.IconKind);
            Assert.AreEqual(mapId, t.MapId);
            Assert.AreEqual(4.5, t.X);
            Assert.AreEqual(6.25, t.Y);
            Assert.AreEqual(sheetId, t.SheetId);
            Assert.IsTrue(t.Hidden);
        }

        [TestMethod]
        public void RoundTrip_Token_NullSheetIdSurvives()
        {
            var mapId = Guid.NewGuid();
            var token = new TokenSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "Ungrouped",
                MapId = mapId,
                SheetId = null,
            };
            var map = SimpleMap(mapId) with { Tokens = [token] };

            var output = RoundTrip(WithMaps(EmptySlot(), [map]));

            Assert.IsNull(output.Maps[0].Tokens[0].SheetId);
        }

        [TestMethod]
        public void RoundTrip_Token_AllIconKindsSurvive()
        {
            // The enum has two values today; this test exists so that adding a
            // third forces the author to look at this assertion and confirm
            // the new value also survives.
            var mapId = Guid.NewGuid();
            var map = SimpleMap(mapId) with
            {
                Tokens =
                [
                    new TokenSnapshot { Id = Guid.NewGuid(), MapId = mapId, IconKind = TokenIconKind.Initial },
                    new TokenSnapshot { Id = Guid.NewGuid(), MapId = mapId, IconKind = TokenIconKind.Solid },
                ],
            };

            var output = RoundTrip(WithMaps(EmptySlot(), [map]));

            var kinds = output.Maps[0].Tokens.Select(t => t.IconKind).ToList();
            CollectionAssert.AreEquivalent(
                new[] { TokenIconKind.Initial, TokenIconKind.Solid },
                kinds);
        }

        // ── SheetSnapshot ─────────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_Sheet_PreservesAllFields()
        {
            var sheetId = Guid.NewGuid();
            var sheet = new SheetSnapshot
            {
                Id = sheetId,
                CharacterName = "Rowan the Bold",
                Notes = "Has a mysterious past.\nLikes apples.",
                Hp = 13,
                MaxHp = 24,
                Values = new Dictionary<string, AttributeValueSnapshot>
                {
                    ["STR"] = new() { Type = AttributeValueType.Score, IntValue = 16 },
                    ["DEX"] = new() { Type = AttributeValueType.Modifier, IntValue = -1 },
                    ["Class"] = new() { Type = AttributeValueType.Text, StringValue = "Ranger" },
                },
                StatusEffects =
                [
                    new StatusEffectSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Name = "Inspired",
                        AppliedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                        MaxHpDelta = 3,
                        OnApplyHpDelta = 0,
                        Notes = "+1d6 to next check",
                        AttributeDeltas =
                        [
                            new AttributeDeltaSnapshot { AttributeName = "DEX", Delta = 1 },
                        ],
                    },
                ],
                RollTemplates =
                [
                    new RollTemplateSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Name = "Bow attack",
                        Label = "1d20+DEX",
                        Mode = RollMode.Disadvantage,
                        FlatModifier = 5,
                        AttributeName = "DEX",
                        Dice = [new DiceTermSnapshot { Count = 1, Sides = 20 }],
                    },
                ],
            };

            var input = EmptySlot() with
            {
                Sheets = [sheet],
                Core = EmptyCore() with { SheetIds = [sheetId] },
            };

            var output = RoundTrip(input);

            Assert.HasCount(1, output.Sheets);
            var s = output.Sheets[0];
            Assert.AreEqual(sheetId, s.Id);
            Assert.AreEqual("Rowan the Bold", s.CharacterName);
            Assert.AreEqual("Has a mysterious past.\nLikes apples.", s.Notes);
            Assert.AreEqual(13, s.Hp);
            Assert.AreEqual(24, s.MaxHp);

            Assert.AreEqual(3, s.Values.Count);
            Assert.AreEqual(16, s.Values["STR"].IntValue);
            Assert.AreEqual(AttributeValueType.Score, s.Values["STR"].Type);
            Assert.AreEqual(-1, s.Values["DEX"].IntValue);
            Assert.AreEqual(AttributeValueType.Modifier, s.Values["DEX"].Type);
            Assert.AreEqual("Ranger", s.Values["Class"].StringValue);
            Assert.AreEqual(AttributeValueType.Text, s.Values["Class"].Type);

            Assert.HasCount(1, s.StatusEffects);
            var eff = s.StatusEffects[0];
            Assert.AreEqual("Inspired", eff.Name);
            Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), eff.AppliedUtc);
            Assert.AreEqual(DateTimeKind.Utc, eff.AppliedUtc.Kind);
            Assert.AreEqual(3, eff.MaxHpDelta);
            Assert.AreEqual(0, eff.OnApplyHpDelta);
            Assert.AreEqual("+1d6 to next check", eff.Notes);
            Assert.HasCount(1, eff.AttributeDeltas);
            Assert.AreEqual("DEX", eff.AttributeDeltas[0].AttributeName);
            Assert.AreEqual(1, eff.AttributeDeltas[0].Delta);

            Assert.HasCount(1, s.RollTemplates);
            var rt = s.RollTemplates[0];
            Assert.AreEqual("Bow attack", rt.Name);
            Assert.AreEqual("1d20+DEX", rt.Label);
            Assert.AreEqual(RollMode.Disadvantage, rt.Mode);
            Assert.AreEqual(5, rt.FlatModifier);
            Assert.AreEqual("DEX", rt.AttributeName);
            Assert.HasCount(1, rt.Dice);
            Assert.AreEqual(20, rt.Dice[0].Sides);
        }

        [TestMethod]
        public void RoundTrip_Sheet_NullHpFieldsSurvive()
        {
            var sheetId = Guid.NewGuid();
            var sheet = new SheetSnapshot
            {
                Id = sheetId,
                CharacterName = "Untracked NPC",
                Hp = null,
                MaxHp = null,
            };
            var input = EmptySlot() with
            {
                Sheets = [sheet],
                Core = EmptyCore() with { SheetIds = [sheetId] },
            };

            var output = RoundTrip(input);

            Assert.IsNull(output.Sheets[0].Hp);
            Assert.IsNull(output.Sheets[0].MaxHp);
        }

        // ── Ordering ──────────────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_TwoMaps_PreservesMapIdsOrder()
        {
            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();
            var idC = Guid.NewGuid();
            // ListOrder explicitly chosen so the desired order (C, A, B) does NOT
            // happen to coincide with the GUID sort.
            var maps = new List<MapSnapshot>
            {
                SimpleMap(idA) with { ListOrder = 1, Name = "A" },
                SimpleMap(idB) with { ListOrder = 2, Name = "B" },
                SimpleMap(idC) with { ListOrder = 0, Name = "C" },
            };

            var input = EmptySlot() with
            {
                Maps = maps,
                Core = EmptyCore() with { MapIds = [idC, idA, idB] },
            };

            var output = RoundTrip(input);

            CollectionAssert.AreEqual(new[] { idC, idA, idB }, output.Core.MapIds.ToList());
            CollectionAssert.AreEqual(new[] { "C", "A", "B" }, output.Maps.Select(m => m.Name).ToList());
        }

        [TestMethod]
        public void RoundTrip_Sheets_PreservesSheetIdsOrder()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();
            var sheets = new List<SheetSnapshot>
            {
                new() { Id = id1, CharacterName = "First" },
                new() { Id = id2, CharacterName = "Second" },
                new() { Id = id3, CharacterName = "Third" },
            };

            var input = EmptySlot() with
            {
                Sheets = sheets,
                Core = EmptyCore() with { SheetIds = [id2, id3, id1] }, // intentional reorder
            };

            var output = RoundTrip(input);

            CollectionAssert.AreEqual(new[] { id2, id3, id1 }, output.Core.SheetIds.ToList());
            CollectionAssert.AreEqual(
                new[] { "Second", "Third", "First" },
                output.Sheets.Select(s => s.CharacterName).ToList());
        }

        // ── Edge cases ────────────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_EmptySlot_Succeeds()
        {
            var output = RoundTrip(EmptySlot());

            Assert.HasCount(0, output.Maps);
            Assert.HasCount(0, output.Sheets);
            Assert.HasCount(0, output.Images);
            Assert.HasCount(0, output.Core.MapIds);
            Assert.HasCount(0, output.Core.SheetIds);
        }

        [TestMethod]
        public void RoundTrip_UnicodeNames_SurviveIntact()
        {
            var mapId = Guid.NewGuid();
            var sheetId = Guid.NewGuid();
            var input = EmptySlot("竜の巣穴 🐉") with
            {
                Maps = [SimpleMap(mapId) with { Name = "María's Tavern" }],
                Sheets = [new() { Id = sheetId, CharacterName = "Σωκράτης" }],
                Core = EmptyCore() with { MapIds = [mapId], SheetIds = [sheetId] },
            };

            var output = RoundTrip(input);

            Assert.AreEqual("竜の巣穴 🐉", output.SlotTitle);
            Assert.AreEqual("María's Tavern", output.Maps[0].Name);
            Assert.AreEqual("Σωκράτης", output.Sheets[0].CharacterName);
        }

        [TestMethod]
        public void RoundTrip_FractionalCoordinates_PreserveDoublePrecision()
        {
            var mapId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var token = new TokenSnapshot { Id = Guid.NewGuid(), MapId = mapId, X = 4.5, Y = 6.25 };
            var image = new MapImageSnapshot
            {
                Id = imageId,
                X = 1.0 / 3.0,        // not exactly representable
                Y = Math.PI,
                Width = 7.7,
                Height = 9.9,
                Rotation = 12.345,
                Opacity = 0.6789,
                ContentType = "image/png",
            };
            var map = SimpleMap(mapId) with { Tokens = [token], Images = [image] };
            var input = WithMaps(
                EmptySlot() with
                {
                    Images = new Dictionary<Guid, VtfPackager.VtfImageAsset>
                    {
                        [imageId] = new VtfPackager.VtfImageAsset("image/png", PngBytes()),
                    },
                },
                [map]);

            var output = RoundTrip(input);
            var t = output.Maps[0].Tokens[0];
            var i = output.Maps[0].Images[0];

            Assert.AreEqual(4.5, t.X);
            Assert.AreEqual(6.25, t.Y);
            Assert.AreEqual(1.0 / 3.0, i.X);
            Assert.AreEqual(Math.PI, i.Y);
            Assert.AreEqual(7.7, i.Width);
            Assert.AreEqual(9.9, i.Height);
            Assert.AreEqual(12.345, i.Rotation);
            Assert.AreEqual(0.6789, i.Opacity);
        }

        // ── Image bytes + archive structure ───────────────────────────────────

        [TestMethod]
        public void RoundTrip_AllImageFormats_BytesAndExtensionsMatch()
        {
            var pngId = Guid.NewGuid();
            var jpgId = Guid.NewGuid();
            var webpId = Guid.NewGuid();
            var pngBytes = PngBytes();
            var jpgBytes = JpegBytes();
            var webpBytes = WebpBytes();

            var images = new Dictionary<Guid, VtfPackager.VtfImageAsset>
            {
                [pngId] = new VtfPackager.VtfImageAsset("image/png", pngBytes),
                [jpgId] = new VtfPackager.VtfImageAsset("image/jpeg", jpgBytes),
                [webpId] = new VtfPackager.VtfImageAsset("image/webp", webpBytes),
            };

            // Pack into a buffer we can inspect for entry names, then unpack
            // the same buffer to check bytes round-trip.
            using var ms = new MemoryStream();
            VtfPackager.Pack(EmptySlot() with { Images = images }, ms);

            ms.Position = 0;
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
            {
                Assert.IsNotNull(zip.GetEntry($"assets/images/{pngId:D}.png"));
                Assert.IsNotNull(zip.GetEntry($"assets/images/{jpgId:D}.jpg"));
                Assert.IsNotNull(zip.GetEntry($"assets/images/{webpId:D}.webp"));
            }

            ms.Position = 0;
            var output = VtfPackager.Unpack(ms);

            Assert.IsTrue(output.Images.ContainsKey(pngId));
            Assert.IsTrue(output.Images.ContainsKey(jpgId));
            Assert.IsTrue(output.Images.ContainsKey(webpId));
            CollectionAssert.AreEqual(pngBytes, output.Images[pngId].Bytes);
            CollectionAssert.AreEqual(jpgBytes, output.Images[jpgId].Bytes);
            CollectionAssert.AreEqual(webpBytes, output.Images[webpId].Bytes);
            Assert.AreEqual("image/png", output.Images[pngId].ContentType);
            Assert.AreEqual("image/jpeg", output.Images[jpgId].ContentType);
            Assert.AreEqual("image/webp", output.Images[webpId].ContentType);
        }

        [TestMethod]
        public void RoundTrip_ManifestVersion_IsCurrentSpec()
        {
            using var ms = new MemoryStream();
            VtfPackager.Pack(EmptySlot(), ms);
            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry("manifest.json");
            Assert.IsNotNull(entry);
            using var s = entry.Open();
            using var reader = new StreamReader(s);
            var json = reader.ReadToEnd();
            // Don't assert on entire shape — just lock the version pin.
            StringAssert.Contains(json, "\"vtfVersion\": \"1.0.0\"");
        }

        // ── Idempotency ───────────────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_Twice_ProducesIdenticalSnapshots()
        {
            var input = RichSlot();
            var first = RoundTrip(input);
            var firstInput = ToPackInput(first, input.SlotTitle, input.Extension);
            var second = RoundTrip(firstInput);

            Assert.AreEqual(NormalizeForCompare(first), NormalizeForCompare(second));
        }

        // ── Golden anti-regression test ───────────────────────────────────────

        [TestMethod]
        public void RoundTrip_FullSlot_JsonGraph_IsLossless()
        {
            // RichSlot() is the canary: every field added to a persisted DTO
            // MUST be populated with a non-default value here. The JSON
            // comparison below then proves the field round-trips. If a future
            // maintainer adds a field to (say) SheetSnapshot but forgets to
            // wire it through the packager, this test fails.
            var input = RichSlot();
            var output = RoundTrip(input);

            var originalJson = NormalizeForCompare(
                input.Core, input.Maps, input.Sheets, input.Images, input.SlotTitle, input.Extension);
            var outputJson = NormalizeForCompare(output);

            Assert.AreEqual(originalJson, outputJson);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions ComparisonJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        private static VtfPackager.UnpackResult RoundTrip(VtfPackager.PackInput input)
        {
            using var ms = new MemoryStream();
            VtfPackager.Pack(input, ms);
            ms.Position = 0;
            return VtfPackager.Unpack(ms);
        }

        private static VtfPackager.PackInput EmptySlot(string title = "Test slot") => new(
            SlotTitle: title,
            Core: EmptyCore(),
            Maps: Array.Empty<MapSnapshot>(),
            Sheets: Array.Empty<SheetSnapshot>(),
            Images: new Dictionary<Guid, VtfPackager.VtfImageAsset>(),
            Extension: new VtfPackager.VtfExtensionPayload(null, DndMapperPhase.Lobby));

        private static LibraryCoreSnapshot EmptyCore() => new()
        {
            SchemaVersion = 4,
            Settings = new DndMapperSettings(),
            AttributeSchema = new AttributeSchemaSnapshot { Preset = AttributePreset.DnD5eCore, Rows = [] },
            ActiveSchemaTemplateId = null,
            InitiativeAttributeName = null,
            CustomTemplates = [],
            GlobalRollTemplates = [],
            MapIds = [],
            SheetIds = [],
        };

        private static MapSnapshot SimpleMap(Guid id) => new()
        {
            Id = id,
            Name = "Map " + id.ToString("D")[..4],
            ListOrder = 0,
            CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Grid = new GridConfig
            {
                WidthCells = 30,
                HeightCells = 20,
                CellPixels = 50,
                ShowGridLines = true,
                SnapToGrid = true,
                LineColor = "#222",
            },
            DefaultSpawnX = null,
            DefaultSpawnY = null,
            Images = [],
            Tokens = [],
            FogMask = [],
        };

        private static VtfPackager.PackInput WithMaps(VtfPackager.PackInput input, IReadOnlyList<MapSnapshot> maps)
        {
            return input with
            {
                Maps = maps,
                Core = input.Core with { MapIds = maps.Select(m => m.Id).ToList() },
            };
        }

        // Minimum-viable PNG (8-byte signature + IHDR for a 1×1 transparent pixel).
        // The packager treats the bytes as opaque, so these aren't required to be
        // valid images — but using realistic headers makes failure diagnostics
        // easier when something does go wrong.
        private static byte[] PngBytes() =>
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR length + type
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1×1
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        ];

        private static byte[] JpegBytes() =>
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, // SOI + APP0
            0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
            0xFF, 0xD9, // EOI
        ];

        private static byte[] WebpBytes() =>
        [
            0x52, 0x49, 0x46, 0x46, 0x1A, 0x00, 0x00, 0x00, // RIFF + size
            0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x4C, // WEBP + VP8L
            0x0D, 0x00, 0x00, 0x00, 0x2F, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        // Build a richly populated PackInput that exercises every persisted
        // field on every persisted DTO. The golden test feeds this into the
        // round trip and JSON-compares the input + output graphs.
        private static VtfPackager.PackInput RichSlot()
        {
            var schemaTemplateId = new Guid("11111111-1111-1111-1111-111111111111");
            var mapA = new Guid("22222222-2222-2222-2222-222222222222");
            var mapB = new Guid("33333333-3333-3333-3333-333333333333");
            var sheet1 = new Guid("44444444-4444-4444-4444-444444444444");
            var sheet2 = new Guid("55555555-5555-5555-5555-555555555555");
            var pngId = new Guid("66666666-6666-6666-6666-666666666666");
            var jpgId = new Guid("77777777-7777-7777-7777-777777777777");
            var webpId = new Guid("88888888-8888-8888-8888-888888888888");
            var token1 = new Guid("99999999-9999-9999-9999-999999999999");
            var token2 = new Guid("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
            var rollId = new Guid("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
            var statusId = new Guid("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");
            var sheetRollId = new Guid("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");
            var sheetStatusId = new Guid("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE");

            var fogMask = new byte[75]; // 30×20 → ceil(600/8) = 75
            for (int i = 0; i < fogMask.Length; i++) fogMask[i] = (byte)(i * 7 % 256);

            var core = new LibraryCoreSnapshot
            {
                SchemaVersion = 4,
                Settings = new DndMapperSettings
                {
                    TokenMovement = TokenMovementPolicy.Anyone,
                    SheetEditByOthers = SheetEditPolicy.OwnersAndHost,
                    RollsVisibleToPlayers = true,
                    PlayersCanCreateNPCs = true,
                    HpTrackingEnabled = true,
                    PlayersCanSeeOtherSheets = false,
                },
                AttributeSchema = new AttributeSchemaSnapshot
                {
                    Preset = AttributePreset.Custom,
                    Rows =
                    [
                        new AttributeRowSnapshot
                        {
                            Name = "STR",
                            Type = AttributeValueType.Score,
                            Default = new AttributeValueSnapshot { Type = AttributeValueType.Score, IntValue = 10 },
                        },
                        new AttributeRowSnapshot
                        {
                            Name = "Notes",
                            Type = AttributeValueType.Text,
                            Default = new AttributeValueSnapshot { Type = AttributeValueType.Text, StringValue = "—" },
                        },
                    ],
                },
                ActiveSchemaTemplateId = schemaTemplateId,
                InitiativeAttributeName = "DEX",
                CustomTemplates =
                [
                    new NamedTemplateSnapshot
                    {
                        Id = schemaTemplateId,
                        Name = "Custom 5e+",
                        IsBuiltIn = false,
                        InitiativeAttributeName = "DEX",
                        Rows =
                        [
                            new AttributeRowSnapshot
                            {
                                Name = "STR",
                                Type = AttributeValueType.Score,
                                Default = new AttributeValueSnapshot { Type = AttributeValueType.Score, IntValue = 10 },
                            },
                        ],
                        StatusEffectTemplates =
                        [
                            new StatusEffectTemplateSnapshot
                            {
                                Id = statusId,
                                Name = "Cursed",
                                MaxHpDelta = -3,
                                OnApplyHpDelta = -1,
                                Notes = "−3 max HP, −1 immediate",
                                AttributeDeltas = [new AttributeDeltaSnapshot { AttributeName = "STR", Delta = -2 }],
                            },
                        ],
                    },
                ],
                GlobalRollTemplates =
                [
                    new RollTemplateSnapshot
                    {
                        Id = rollId,
                        Name = "House attack",
                        Label = "1d20+STR",
                        Mode = RollMode.Advantage,
                        FlatModifier = 2,
                        AttributeName = "STR",
                        Dice = [new DiceTermSnapshot { Count = 1, Sides = 20 }],
                    },
                ],
                MapIds = [mapB, mapA], // intentional reorder (B first)
                SheetIds = [sheet2, sheet1],
            };

            var maps = new List<MapSnapshot>
            {
                new()
                {
                    Id = mapA,
                    Name = "Lair",
                    ListOrder = 1,
                    CreatedUtc = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
                    Grid = new GridConfig
                    {
                        WidthCells = 30,
                        HeightCells = 20,
                        CellPixels = 50,
                        ShowGridLines = true,
                        SnapToGrid = true,
                        LineColor = "#10203080",
                    },
                    DefaultSpawnX = 1.5,
                    DefaultSpawnY = 2.5,
                    FogMask = fogMask,
                    Images =
                    [
                        new MapImageSnapshot
                        {
                            Id = pngId,
                            Name = "background",
                            ContentType = "image/png",
                            X = 0,
                            Y = 0,
                            Width = 30,
                            Height = 20,
                            OriginalWidth = 30,
                            OriginalHeight = 20,
                            Rotation = 0,
                            Opacity = 1.0,
                            LayerOrder = 0,
                            Locked = true,
                            Hidden = false,
                            ByteSize = PngBytes().Length,
                        },
                        new MapImageSnapshot
                        {
                            Id = jpgId,
                            Name = "prop",
                            ContentType = "image/jpeg",
                            X = 5,
                            Y = 5,
                            Width = 2.5,
                            Height = 2.5,
                            OriginalWidth = 5,
                            OriginalHeight = 5,
                            Rotation = 90,
                            Opacity = 0.75,
                            LayerOrder = 1,
                            Locked = false,
                            Hidden = false,
                            ByteSize = JpegBytes().Length,
                        },
                    ],
                    Tokens =
                    [
                        new TokenSnapshot
                        {
                            Id = token1,
                            Name = "Goblin",
                            Color = "#669944",
                            IconKind = TokenIconKind.Initial,
                            MapId = mapA,
                            X = 4.5,
                            Y = 6.25,
                            SheetId = sheet1,
                            Hidden = false,
                        },
                    ],
                },
                new()
                {
                    Id = mapB,
                    Name = "Tavern",
                    ListOrder = 0,
                    CreatedUtc = new DateTime(2026, 5, 2, 11, 0, 0, DateTimeKind.Utc),
                    Grid = new GridConfig
                    {
                        WidthCells = 20,
                        HeightCells = 15,
                        CellPixels = 60,
                        ShowGridLines = false,
                        SnapToGrid = false,
                        LineColor = "#000",
                    },
                    DefaultSpawnX = null,
                    DefaultSpawnY = null,
                    FogMask = [],
                    Images =
                    [
                        new MapImageSnapshot
                        {
                            Id = webpId,
                            Name = "floor",
                            ContentType = "image/webp",
                            X = 1,
                            Y = 1,
                            Width = 18,
                            Height = 13,
                            OriginalWidth = 18,
                            OriginalHeight = 13,
                            Rotation = 0,
                            Opacity = 1.0,
                            LayerOrder = 0,
                            Locked = false,
                            Hidden = true,
                            ByteSize = WebpBytes().Length,
                        },
                    ],
                    Tokens =
                    [
                        new TokenSnapshot
                        {
                            Id = token2,
                            Name = "Innkeeper",
                            Color = "#aabbcc",
                            IconKind = TokenIconKind.Solid,
                            MapId = mapB,
                            X = 10,
                            Y = 7,
                            SheetId = null,
                            Hidden = true,
                        },
                    ],
                },
            };

            var sheets = new List<SheetSnapshot>
            {
                new()
                {
                    Id = sheet1,
                    CharacterName = "Goblin scout",
                    Notes = "Carries a rusty short sword.",
                    Hp = 6,
                    MaxHp = 12,
                    Values = new Dictionary<string, AttributeValueSnapshot>
                    {
                        ["STR"] = new() { Type = AttributeValueType.Score, IntValue = 8 },
                        ["Description"] = new() { Type = AttributeValueType.Text, StringValue = "Sneaky" },
                    },
                    StatusEffects =
                    [
                        new StatusEffectSnapshot
                        {
                            Id = sheetStatusId,
                            Name = "Limping",
                            AppliedUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
                            MaxHpDelta = 0,
                            OnApplyHpDelta = -1,
                            Notes = "Halved speed",
                            AttributeDeltas = [new AttributeDeltaSnapshot { AttributeName = "STR", Delta = -1 }],
                        },
                    ],
                    RollTemplates =
                    [
                        new RollTemplateSnapshot
                        {
                            Id = sheetRollId,
                            Name = "Stab",
                            Label = "1d4+STR",
                            Mode = RollMode.Normal,
                            FlatModifier = 0,
                            AttributeName = "STR",
                            Dice = [new DiceTermSnapshot { Count = 1, Sides = 4 }],
                        },
                    ],
                },
                new()
                {
                    Id = sheet2,
                    CharacterName = "Innkeeper",
                    Notes = "",
                    Hp = null,
                    MaxHp = null,
                    Values = new Dictionary<string, AttributeValueSnapshot>(),
                    StatusEffects = [],
                    RollTemplates = [],
                },
            };

            var images = new Dictionary<Guid, VtfPackager.VtfImageAsset>
            {
                [pngId] = new VtfPackager.VtfImageAsset("image/png", PngBytes()),
                [jpgId] = new VtfPackager.VtfImageAsset("image/jpeg", JpegBytes()),
                [webpId] = new VtfPackager.VtfImageAsset("image/webp", WebpBytes()),
            };

            return new VtfPackager.PackInput(
                SlotTitle: "Rich golden slot",
                Core: core,
                Maps: maps,
                Sheets: sheets,
                Images: images,
                Extension: new VtfPackager.VtfExtensionPayload(null, DndMapperPhase.Lobby));
        }

        private static string NormalizeForCompare(VtfPackager.UnpackResult r)
            => NormalizeForCompare(r.Core, r.Maps, r.Sheets, r.Images, r.SlotTitle, r.Extension);

        private static string NormalizeForCompare(
            LibraryCoreSnapshot core,
            IReadOnlyList<MapSnapshot> maps,
            IReadOnlyList<SheetSnapshot> sheets,
            IReadOnlyDictionary<Guid, VtfPackager.VtfImageAsset> images,
            string slotTitle,
            VtfPackager.VtfExtensionPayload extension)
        {
            // Project to an order-stable shape and serialize. Collection order
            // is canonicalized to Id-sorted here so the *contents* comparison is
            // order-independent — ordering itself is covered by the dedicated
            // RoundTrip_*_PreservesOrder tests. Without this, the input's Maps
            // list (declaration order) would not match the output's (which
            // Unpack reorders to match MapIds).
            var projection = new
            {
                SlotTitle = slotTitle,
                Core = core,
                Maps = maps.OrderBy(m => m.Id).ToList(),
                Sheets = sheets.OrderBy(s => s.Id).ToList(),
                Images = images
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new
                    {
                        Id = kv.Key,
                        kv.Value.ContentType,
                        Bytes = Convert.ToBase64String(kv.Value.Bytes),
                    })
                    .ToList(),
                Extension = extension,
            };
            return JsonSerializer.Serialize(projection, ComparisonJson);
        }

        // Convert an UnpackResult back into a PackInput so the second leg of
        // the idempotency test can run.
        private static VtfPackager.PackInput ToPackInput(
            VtfPackager.UnpackResult r, string slotTitle, VtfPackager.VtfExtensionPayload extension)
            => new(slotTitle, r.Core, r.Maps, r.Sheets, r.Images, extension);
    }
}

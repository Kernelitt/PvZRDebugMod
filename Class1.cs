using ImGuiNET;
using KrutolFramework.Core;
using OpenTK.Windowing.Desktop;
using PVZRemake;
using PVZRemake.Board;
using PVZRemake.Scenes;

namespace DebugMenuMod
{
    public static class ModEntry
    {
        private static ImGuiController _imGuiController;
        private static GameWindow _window;
        private static bool _showDebugMenu = true;

        // Состояния для вкладки Спавнера
        private static int _spawnRow = 0;
        private static int _spawnCol = 0;
        private static int _selectedPlantTypeIdx = 0;
        private static int _selectedZombieTypeIdx = 0;

        // Массивы типов для выпадающих списков Combo
        private static readonly PlantType[] PlantTypes = (PlantType[])Enum.GetValues(typeof(PlantType));
        private static readonly ZombieType[] ZombieTypes = (ZombieType[])Enum.GetValues(typeof(ZombieType));
        private static string[] _plantTypeNames;
        private static string[] _zombieTypeNames;

        // Глобальные переменные для отключения КД (синхронизируем с игрой)
        private static bool _noCooldowns = false;

        public static void Init(GameWindow window)
        {
            _window = window;
            _imGuiController = new ImGuiController(_window.ClientSize.X, _window.ClientSize.Y);
            _imGuiController.SetupInput(_window);

            _imGuiController.OnBuildUI += BuildDebugMenu;

            // Кэшируем строковые имена для ImGui Combo
            _plantTypeNames = PlantTypes.Select(p => p.ToString()).ToArray();
            _zombieTypeNames = ZombieTypes.Select(z => z.ToString()).ToArray();

            ModManager.OnUpdate += OnUpdate;
            ModManager.OnRender += OnRender;
            ModManager.OnResize += OnResize;
            _window.Unload += OnWindowUnload;
        }

        private static void OnUpdate(float dt) => _imGuiController?.Update(_window, dt);
        private static void OnRender() => _imGuiController?.Render();
        private static void OnResize(int w, int h) => _imGuiController?.WindowResized(w, h);

        private static void BuildDebugMenu()
        {
            if (!_showDebugMenu) return;

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(550, 450), ImGuiCond.FirstUseEver);

            if (ImGui.Begin("Kernelit Developer Debug Menu", ref _showDebugMenu, ImGuiWindowFlags.NoCollapse))
            {
                var board = BoardScene.Instance;

                // Базовые утилиты профиля (доступны везде)
                if (LawnApp.CurrentUser != null)
                {
                    int coins = (int)LawnApp.CurrentUser.Coins;
                    ImGui.SetNextItemWidth(120);
                    if (ImGui.InputInt("Wallet ($)", ref coins, 100, 1000)) LawnApp.CurrentUser.Coins = Math.Max(0, coins);
                    ImGui.SameLine();
                    ImGui.Text($"| Profile ID: {LawnApp.CurrentUser.UserId}");
                }

                ImGui.Separator();

                // Создаем вкладки меню
                if (ImGui.BeginTabBar("DebugTabs"))
                {
                    // ВКЛАДКА 1: Управление матчем и читы
                    if (ImGui.BeginTabItem("Match & Cheats"))
                    {
                        if (board != null)
                        {
                            ImGui.Text($"Active Level: {board.CurrentLevelName}");

                            if (board.SeedBank != null)
                            {
                                int sun = board.SeedBank.SunAmount;
                                if (ImGui.SliderInt("Sun Amount", ref sun, 0, 9990)) board.SeedBank.SunAmount = sun;
                            }

                            ImGui.Spacing();
                            ImGui.Separator();
                            ImGui.Text("Instant Game Over Triggers:");

                            if (ImGui.Button("FORCE VICTORY (Win Level)", new System.Numerics.Vector2(200, 30)))
                            {
                                board.ForceSetLevelState(GameLevelState.LevelWon);
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("FORCE DEFEAT (Lose Level)", new System.Numerics.Vector2(200, 30)))
                            {
                                board.ForceSetLevelState(GameLevelState.LevelLost);
                            }

                            ImGui.Separator();
                        }
                        else
                        {
                            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f), "Game board is inactive (You are in Main Menu).");
                        }

                        // Управление Кулдауном (работает через внедренное свойство SeedCard)
                        if (ImGui.Checkbox("Disable Seed Cards Cooldown (No CD)", ref _noCooldowns))
                        {
                            SeedCard.DisableCooldownsCheat = _noCooldowns;
                        }

                        ImGui.EndTabItem();
                    }

                    // ВКЛАДКА 2: Спавнер растений и зомби по клеткам
                    if (ImGui.BeginTabItem("Grid Spawner"))
                    {
                        if (board != null)
                        {
                            ImGui.Text("Select Coordinates:");
                            ImGui.SliderInt("Row (Линия)", ref _spawnRow, 0, Grid.TotalRows - 1);
                            ImGui.SliderInt("Col (Клетка 0-8)", ref _spawnCol, 0, 8);

                            ImGui.Separator();

                            // Спавн растений
                            ImGui.Text("Plant Spawner:");
                            ImGui.Combo("Plant Type", ref _selectedPlantTypeIdx, _plantTypeNames, _plantTypeNames.Length);
                            if (ImGui.Button("Spawn Plant on Cell"))
                            {
                                PlantType typeToSpawn = PlantTypes[_selectedPlantTypeIdx];
                                // Проверяем занята ли клетка, чтобы не сажать друг в друга
                                if (!board.Plants.Exists(p => p.Row == _spawnRow && p.Col == _spawnCol))
                                {
                                    var newPlant = PlantFactory.CreatePlant(typeToSpawn, _spawnRow, _spawnCol);
                                    if (newPlant != null) board.Plants.Add(newPlant);
                                }
                            }

                            ImGui.Separator();

                            // Спавн зомби
                            ImGui.Text("Zombie Spawner:");
                            ImGui.Combo("Zombie Type", ref _selectedZombieTypeIdx, _zombieTypeNames, _zombieTypeNames.Length);
                            if (ImGui.Button("Spawn Zombie on Row"))
                            {
                                ZombieType typeToSpawn = ZombieTypes[_selectedZombieTypeIdx];
                                // Рассчитываем стартовую позицию клетки по сетке для спавна в конкретную координату X
                                GridCoords coords = Grid.GetCoords(_spawnRow, _spawnCol);
                                float spawnX = coords.CellTopLeft.X;

                                var newZombie = ZombieFactory.CreateZombie(typeToSpawn, _spawnRow, spawnX);
                                if (newZombie != null) board.Zombies.Add(newZombie);
                            }
                        }
                        else
                        {
                            ImGui.Text("Spawner requires an active game level.");
                        }

                        ImGui.EndTabItem();
                    }

                    // ВКЛАДКА 3: Изменение глобальных баз данных (Листов характеристик)
                    if (ImGui.BeginTabItem("Database Editor"))
                    {
                        ImGui.Text("Edit Plant Configurations (Global Database):");
                        if (ImGui.BeginChild("PlantDbScroll", new System.Numerics.Vector2(0, 160), ImGuiChildFlags.None))
                        {
                            foreach (PlantType pType in PlantTypes)
                            {
                                var pDef = PlantDatabase.Get(pType);
                                if (pDef == null) continue;

                                if (ImGui.TreeNode($"{pType} Settings"))
                                {
                                    int hp = pDef.MaxHealth;
                                    if (ImGui.InputInt("Max Health", ref hp)) pDef.MaxHealth = hp;

                                    int cost = pDef.SunCost;
                                    if (ImGui.InputInt("Sun Cost", ref cost)) pDef.SunCost = cost;

                                    float cd = pDef.RefreshTime;
                                    if (ImGui.InputFloat("Base Refresh Time", ref cd, 0.5f)) pDef.RefreshTime = Math.Max(0f, cd);

                                    float rate = pDef.LaunchRate;
                                    if (ImGui.InputFloat("Attack Rate (Launch)", ref rate, 0.1f)) pDef.LaunchRate = Math.Max(0f, rate);

                                    ImGui.TreePop();
                                    ImGui.Separator();
                                }
                            }
                            ImGui.EndChild();
                        }

                        ImGui.Separator();

                        ImGui.Text("Edit Zombie Configurations (Global Database):");
                        if (ImGui.BeginChild("ZombieDbScroll", new System.Numerics.Vector2(0, 160), ImGuiChildFlags.None))
                        {
                            foreach (ZombieType zType in ZombieTypes)
                            {
                                var zDef = ZombieDatabase.Get(zType);
                                if (zDef == null) continue;
                                if (ImGui.TreeNode($"{zDef.InternalName} Constants"))
                                {
                                    int weight = zDef.Weight;
                                    if (ImGui.InputInt("Spawn Weight", ref weight)) zDef.Weight = Math.Max(0, weight);
                                    int pts = zDef.Points;
                                    if (ImGui.InputInt("Wave Cost Points", ref pts)) zDef.Points = Math.Max(1, pts);
                                    int wave = zDef.StartingWave;
                                    if (ImGui.InputInt("First Appearance Wave", ref wave)) zDef.StartingWave = Math.Max(1, wave);
                                    ImGui.TreePop();
                                    ImGui.Separator();
                                }
                            }
                            ImGui.EndChild();
                        }
                        ImGui.EndTabItem();
                    }
                    // ВКЛАДКА 4: Список живых сущностей на поле (Инспектор реального времени)
                    if (ImGui.BeginTabItem("Live Inspector"))
                    {
                        if (board != null)
                        {
                            ImGui.Text($"Plants on Lawn ({board.Plants.Count}):");
                            if (ImGui.BeginChild("LivePlants", new System.Numerics.Vector2(0, 150), ImGuiChildFlags.None))
                            {
                                for (int i = board.Plants.Count - 1; i >= 0; i--)
                                {
                                    var plant = board.Plants[i];
                                    ImGui.Text($"[{i}] {plant.Type} (Row: {plant.Row}, Col: {plant.Col})");
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Kill##p{i}")) plant.TakeDamage(99999);
                                }
                                ImGui.EndChild();
                            }
                            ImGui.Separator();
                            ImGui.Text($"Zombies on Lawn ({board.Zombies.Count}):");
                            if (ImGui.BeginChild("LiveZombies", new System.Numerics.Vector2(0, 150), ImGuiChildFlags.None))
                            {
                                for (int i = board.Zombies.Count - 1; i >= 0; i--)
                                {
                                    var zombie = board.Zombies[i];
                                    ImGui.Text($"[{i}] {zombie.Type} (Row: {zombie.Row}, X: {zombie.X:F1}) HP: {zombie.Health:F0}");
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Kill##z{i}")) zombie.TakeDamage(99999, PlantDamageType.Instant);
                                }
                                ImGui.EndChild();
                            }
                        }
                        else
                        {
                            ImGui.Text("No live entities found outside gameplay session.");
                        }
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                ImGui.End();
            }
        }
        private static void OnWindowUnload()
        {
            if (_imGuiController != null) _imGuiController.OnBuildUI -= BuildDebugMenu;
            ModManager.OnUpdate -= OnUpdate;
            ModManager.OnRender -= OnRender;
            ModManager.OnResize -= OnResize;
            _imGuiController?.Dispose();
        }
    }
}
using System;

class Program
{
    static int Main(string[] args)
    {
        bool unitOnly = args.Length == 1 && args[0] == "--unit-only";
        if (args.Length == 2 && args[0] == "--write-population-fixtures") { PopulationScalingBenchmarks.Write(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--write-keybinding-fixtures") { KeybindingFixtures.Write(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--write-turn-fixtures") { TurnPerformanceBenchmarks.Write(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--write-density-fixtures") { TurnPerformanceBenchmarks.WriteMatrix(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--write-catchup-fixtures") { TurnPerformanceBenchmarks.WriteCatchup(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--write-fire-catchup-fixture") { TurnPerformanceBenchmarks.WriteFireCatchup(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--bench-catchup") { TurnPerformanceBenchmarks.RunCatchup(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--bench-turns") { TurnPerformanceBenchmarks.Run(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--bench-native-turns") { NativeTurnPerformanceBenchmarks.Run(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--write-community-fixture") { CommunityCompatibilityFixture.Write(args[1]); return 0; }
        if (args.Length == 3 && args[0] == "--save-budget-worker") return SaveBudgetRunner.RunWorker(args[1], args[2]);
        if (args.Length == 2 && args[0] == "--check-save-budget") { SaveBudgetRunner.Run(args[1]); return 0; }
        ScenarioRunner.RegisterAll();
        if (args.Length == 2 && args[0] == "--scenario-list") {
            int failures = 0;
            foreach (string name in System.IO.File.ReadAllLines(args[1])) if (!String.IsNullOrWhiteSpace(name) && ScenarioRunner.Run(name.Trim()) != 0) failures++;
            Console.WriteLine("Selected scenario failures: " + failures); return failures == 0 ? 0 : 1;
        }
        SkillScenario.AssertCoverage();
        if (args.Length == 2 && args[0] == "--audit-save")
        {
            SaveStructureAudit.Run(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--bench-save")
        {
            SavePerformanceBenchmarks.Run(args[1]);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--bench")
        {
            PerformanceBenchmarks.Run();
            return 0;
        }
        if (args.Length == 1 && args[0] == "--bench-ai")
        {
            AIAndGenerationBenchmarks.Run();
            return 0;
        }
        if (args.Length == 1 && args[0] == "--bench-xpd")
        {
            XpdBaseBenchmarks.Run();
            return 0;
        }
        if (args.Length == 1 && !unitOnly)
        {
            if (args[0] == "--list") { ScenarioRunner.List(); return 0; }
            return ScenarioRunner.Run(args[0]);
        }
        if (args.Length != 0 && !unitOnly)
        {
            Console.Error.WriteLine("Usage: UnitTests.exe [--unit-only|--list|--all|--all-continue|--bench|--bench-ai|--bench-xpd|--write-turn-fixtures directory|--bench-turns copied-fixture-path|--bench-save copied-save-path|--check-save-budget copied-save-path|--audit-save copied-save-path|scenario-name]");
            return 2;
        }
        GameTests.Run();
        MouseMoveTests.Run();
        AITests.Run();
        RulesTests.Run();
        GenerationTests.Run();
        CatalogTests.Run();
        ModelIdTests.Run();
        CommandCatalogTests.Run();
        RandomStateTests.Run();
        SaveStoreTests.Run();
        SaveBudgetTests.Run();
        SaveStructureAuditTests.Run();
        SaveGameVersionTests.Run();
        XpdFoodOrderMigrationTests.Run();
        HintsSaveTests.Run();
        InputReaderTests.Run();
        SimulationWorkerTests.Run();
        GDIPlusCanvasResourceTests.Run();
        GameImagesGrayLevelTests.Run();
        GameImagesRealAssetTests.Run();
        FovRadiusEquivalenceTests.Run();
        OverlayCollectionTests.Run();
        ManualNavigatorTests.Run();
        MovementScenarioTests.Run();
        if (!unitOnly && ScenarioRunner.Run("--all") != 0) return 1;
        Console.WriteLine("All unit tests passed");
        return 0;
    }
}

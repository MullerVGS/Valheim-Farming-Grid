using System.Collections.Generic;
using FarmingGrid.Core;
using Xunit;

namespace FarmingGrid.Tests
{
    public class GridSolverTests
    {
        private const float Grow = 0.5f;
        private const float Body = 0.1f;
        private const float Cell = Grow + Body; // exact rule between two identical crops

        private static Crop CropAt(float x, float z) => new Crop(new Vec2(x, z), Grow, Body);

        private static SnapSettings Settings() => new SnapSettings();

        private static void Near(Vec2 expected, Vec2 actual)
        {
            Assert.InRange(actual.X, expected.X - 1e-3f, expected.X + 1e-3f);
            Assert.InRange(actual.Z, expected.Z - 1e-3f, expected.Z + 1e-3f);
        }

        [Fact]
        public void Without_neighbours_placement_is_free()
        {
            var result = GridSolver.Solve(CropAt(3f, 4f), new List<Crop>(), Settings());

            Assert.False(result.Snapped);
            Assert.False(result.Crowded);
            Near(new Vec2(3f, 4f), result.Position);
        }

        [Fact]
        public void Second_sapling_orbits_the_first_toward_the_aim()
        {
            var nearby = new List<Crop> { CropAt(0f, 0f) };

            var result = GridSolver.Solve(CropAt(0.3f, 0.3f), nearby, Settings());

            Assert.True(result.Snapped);
            float d = Cell / (float)System.Math.Sqrt(2);
            Near(new Vec2(d, d), result.Position);
        }

        [Fact]
        public void Third_sapling_follows_the_axis_of_the_first_two()
        {
            var nearby = new List<Crop> { CropAt(0f, 0f), CropAt(Cell, 0f) };

            var result = GridSolver.Solve(CropAt(0.1f, 0.5f), nearby, Settings());

            Assert.True(result.Snapped);
            Near(new Vec2(0f, Cell), result.Position);
        }

        [Fact]
        public void Skips_occupied_points_and_picks_the_nearest_free_one()
        {
            var nearby = new List<Crop> { CropAt(0f, 0f), CropAt(Cell, 0f), CropAt(2 * Cell, 0f) };

            // Aiming at the middle one: the nearest free point is on the row above or below.
            var result = GridSolver.Solve(CropAt(Cell, 0.2f), nearby, Settings());

            Assert.True(result.Snapped);
            Assert.False(result.Crowded);
            Near(new Vec2(Cell, Cell), result.Position);
        }

        [Fact]
        public void Rotated_grid_keeps_the_field_angle()
        {
            var axis = Vec2.FromAngle(30f);
            var nearby = new List<Crop> { CropAt(0f, 0f), new Crop(axis * Cell, Grow, Body) };
            var target = axis.Perpendicular * Cell;

            var result = GridSolver.Solve(new Crop(target + new Vec2(0.05f, -0.05f), Grow, Body), nearby, Settings());

            Near(target, result.Position);
        }

        [Fact]
        public void Fixed_orientation_ignores_the_aim_direction()
        {
            var settings = Settings();
            settings.Orientation = GridOrientation.Fixed;
            settings.FixedAngle = 90f; // U axis points to +X
            var nearby = new List<Crop> { CropAt(0f, 0f) };

            var result = GridSolver.Solve(CropAt(0.4f, 0.4f), nearby, settings);

            Near(new Vec2(Cell, Cell), result.Position);
        }

        [Fact]
        public void Far_from_the_field_does_not_snap_to_the_grid()
        {
            var nearby = new List<Crop> { CropAt(0f, 0f) };

            var result = GridSolver.Solve(CropAt(Cell * 3f, 0f), nearby, Settings());

            Assert.False(result.Snapped);
            Assert.False(result.Crowded);
        }

        [Fact]
        public void Free_placement_on_top_of_another_crop_is_crowded()
        {
            var settings = Settings();
            settings.ReachCells = 0f;
            var nearby = new List<Crop> { CropAt(0f, 0f) };

            var result = GridSolver.Solve(CropAt(0.2f, 0f), nearby, settings);

            Assert.False(result.Snapped);
            Assert.True(result.Crowded);
        }

        [Fact]
        public void Surrounded_by_crops_is_crowded()
        {
            var settings = Settings();
            settings.SearchRadius = 0;
            var nearby = new List<Crop> { CropAt(0f, 0f), CropAt(Cell, 0f) };

            var result = GridSolver.Solve(CropAt(Cell, 0.05f), nearby, settings);

            Assert.True(result.Snapped);
            Assert.True(result.Crowded);
        }

        [Fact]
        public void Extra_spacing_widens_the_grid()
        {
            var settings = Settings();
            settings.ExtraSpacing = 0.4f;
            var nearby = new List<Crop> { CropAt(0f, 0f), CropAt(Cell + 0.4f, 0f) };

            var result = GridSolver.Solve(CropAt(0f, 0.9f), nearby, settings);

            Near(new Vec2(0f, Cell + 0.4f), result.Position);
        }

        [Fact]
        public void Exact_rule_uses_the_grow_radius_against_the_other_body()
        {
            var small = new Crop(Vec2.Zero, 0.5f, 0.1f);
            var large = new Crop(Vec2.Zero, 1.0f, 0.3f);

            Assert.Equal(1.1f, Spacing.Required(small, large, SpacingRule.Exact, 0f), 3);
            Assert.Equal(2.0f, Spacing.Required(small, large, SpacingRule.Wide, 0f), 3);
        }

        [Fact]
        public void Exactly_at_minimum_distance_does_not_conflict()
        {
            var a = CropAt(0f, 0f);
            var b = CropAt(Cell, 0f);

            Assert.False(Spacing.Conflicts(a, b, SpacingRule.Exact, 0f));
            Assert.True(Spacing.Conflicts(a, CropAt(Cell - 0.01f, 0f), SpacingRule.Exact, 0f));
        }

        private const int Garden = 1;
        private const int Wild = 0;

        [Fact]
        public void Tree_near_the_garden_does_not_anchor_the_grid()
        {
            // Grown tree: a "ripe" harvest of a large-radius sapling, from another family.
            var tree = Crop.Grown(new Vec2(0.5f, 0f), bodyRadius: 0.3f, saplingGrow: 2.5f, saplingBody: 0.2f, family: Wild);
            var ghost = new Crop(new Vec2(-0.5f, 0f), Grow, Body, Garden);

            var result = GridSolver.Solve(ghost, new List<Crop> { tree }, Settings());

            Assert.False(result.Snapped);
        }

        [Fact]
        public void Grown_crops_only_occupy_with_their_body()
        {
            // Grown bush at 1 m: the 0.5-radius sapling grows, even though the bush came from a 2.5-radius sapling.
            var bush = Crop.Grown(new Vec2(1f, 0f), bodyRadius: 0.3f, saplingGrow: 2.5f, saplingBody: 0.2f, family: Wild);
            var ghost = new Crop(Vec2.Zero, Grow, Body, Garden);

            Assert.False(GridSolver.Solve(ghost, new List<Crop> { bush }, Settings()).Crowded);
            Assert.True(GridSolver.Solve(ghost.At(new Vec2(0.7f, 0f)), new List<Crop> { bush }, Settings()).Crowded);
        }

        [Fact]
        public void Grown_harvest_keeps_the_planted_field_step()
        {
            // Pickable onions in a row at the sapling step.
            var nearby = new List<Crop>
            {
                Crop.Grown(new Vec2(0f, 0f), Body, Grow, Body, Garden),
                Crop.Grown(new Vec2(Cell, 0f), Body, Grow, Body, Garden),
            };

            var result = GridSolver.Solve(new Crop(new Vec2(0.05f, 0.55f), Grow, Body, Garden), nearby, Settings());

            Near(new Vec2(0f, Cell), result.Position);
        }

        [Fact]
        public void Step_two_skips_every_other_cell()
        {
            var nearby = new List<Crop> { CropAt(0f, 0f), CropAt(Cell, 0f) };

            // Aiming one cell above the first crop lands two cells above it.
            var result = GridSolver.Solve(CropAt(0.05f, Cell * 1.2f), nearby, new SnapSettings { Step = 2 });

            Assert.True(result.Snapped);
            Assert.False(result.Crowded);
            Near(new Vec2(0f, 2 * Cell), result.Position);
        }

        [Fact]
        public void Step_keeps_the_axis_of_a_field_planted_at_step_one()
        {
            var axis = Vec2.FromAngle(30f);
            var nearby = new List<Crop> { CropAt(0f, 0f), new Crop(axis * Cell, Grow, Body) };
            var target = axis.Perpendicular * (3 * Cell);

            var result = GridSolver.Solve(new Crop(target + new Vec2(0.1f, -0.1f), Grow, Body), nearby, new SnapSettings { Step = 3 });

            Near(target, result.Position);
        }
    }
}

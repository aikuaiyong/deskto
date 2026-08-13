using Deskto.Core.Config;

namespace Deskto.Core.Tests;

internal static class ConfigTests
{
    public static void Register()
    {
        TestHarness.Register("配置序列化往返保留所有模块", RoundTripPreservesAll);
        TestHarness.Register("缺失/多余字段不崩溃且回退默认", TolerantToMissingFields);
        TestHarness.Register("中文内容不被转义破坏", ChineseSurvivesRoundTrip);
    }

    private static void RoundTripPreservesAll()
    {
        var cfg = new AppConfig
        {
            Partitions =
            {
                new PartitionConfig { Title = "工作", X = 10, Y = 20, Width = 300, Height = 200,
                    TitleOpacity = 0.9, ContentOpacity = 0.5 },
            },
            Todos =
            {
                new TodoItem { Text = "写文案", Done = false },
                new TodoItem { Text = "开会", Done = true },
            },
            Labels =
            {
                new ColorLabel { Text = "紧急", FontSize = 18, BgColor = "#FF0000" },
            },
            LaunchBar = { AutoHide = true, Pinned = false, Items = { new LaunchItem { Name = "记事本", Path = "notepad.exe" } } },
        };
        cfg.Schedule.Set(DayOfWeek.Monday, "周会");
        cfg.Settings.Layer = LayerMode.BehindIcons;

        var json = ConfigStore.Serialize(cfg);
        var back = ConfigStore.Deserialize(json);

        TestHarness.Equal(1, back.Partitions.Count);
        TestHarness.Equal("工作", back.Partitions[0].Title);
        TestHarness.Equal(300.0, back.Partitions[0].Width);
        TestHarness.Equal(0.5, back.Partitions[0].ContentOpacity);
        TestHarness.Equal(2, back.Todos.Count);
        TestHarness.Equal("写文案", back.Todos[0].Text);
        TestHarness.True(back.Todos[1].Done);
        TestHarness.Equal("紧急", back.Labels[0].Text);
        TestHarness.Equal(18.0, back.Labels[0].FontSize);
        TestHarness.Equal("周会", back.Schedule.Get(DayOfWeek.Monday));
        TestHarness.Equal(LayerMode.BehindIcons, back.Settings.Layer);
        TestHarness.Equal(1, back.LaunchBar.Items.Count);
        TestHarness.Equal("notepad.exe", back.LaunchBar.Items[0].Path);
    }

    private static void TolerantToMissingFields()
    {
        var json = "{ \"Partitions\": [ { \"Title\": \"只给标题\" } ], \"Todos\": [ { \"Text\": \"x\" } ] }";
        var cfg = ConfigStore.Deserialize(json);
        TestHarness.Equal(1, cfg.Partitions.Count);
        TestHarness.Equal("只给标题", cfg.Partitions[0].Title);
        TestHarness.Equal(260.0, cfg.Partitions[0].Width); // 回退默认
        TestHarness.Equal(1, cfg.Todos.Count);
    }

    private static void ChineseSurvivesRoundTrip()
    {
        var cfg = new AppConfig
        {
            Partitions = { new PartitionConfig { Title = "我的分区" } },
            Todos = { new TodoItem { Text = "写一周文案 📝" } },
        };
        var json = ConfigStore.Serialize(cfg);
        TestHarness.True(json.Contains("我的分区"));
        TestHarness.True(json.Contains("写一周文案"));
        var back = ConfigStore.Deserialize(json);
        TestHarness.Equal("我的分区", back.Partitions[0].Title);
        TestHarness.Equal("写一周文案 📝", back.Todos[0].Text);
    }
}

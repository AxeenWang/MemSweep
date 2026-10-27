using MemSweep.Core;

namespace MemSweep.Core.Tests;

[TestClass]
public sealed class SettingsFormTests
{
    private static SettingsForm Form(string orange, string red, string retention, bool autoStart = false) =>
        new(AppSettings.Default) { OrangeText = orange, RedText = red, RetentionText = retention, AutoStart = autoStart };

    [TestMethod]
    public void StartsWithTheCurrentSettings()
    {
        var form = new SettingsForm(new AppSettings(60, 90, true, 30));

        Assert.AreEqual("60", form.OrangeText);
        Assert.AreEqual("90", form.RedText);
        Assert.IsTrue(form.AutoStart);
        Assert.AreEqual("30", form.RetentionText);
        Assert.IsNull(form.Error);
    }

    [TestMethod]
    public void ValidInputGivesTheSettings()
    {
        var form = Form("65", "80", "7", autoStart: true);

        Assert.AreEqual(new AppSettings(65, 80, true, 7), form.TryRead());
        Assert.IsNull(form.Error);
    }

    [TestMethod]
    [DataRow("1", "2", "1")]
    [DataRow("99", "100", "3650")]
    public void BoundaryValuesAreAccepted(string orange, string red, string retention)
    {
        var settings = Form(orange, red, retention).TryRead();

        Assert.IsNotNull(settings);
        Assert.IsTrue(settings.IsValid);
    }

    [TestMethod]
    public void FullWidthDigitsAndSurroundingSpacesAreAccepted()
    {
        var form = Form("７０", " 85 ", "９０");

        Assert.AreEqual(new AppSettings(70, 85, false, 90), form.TryRead());
    }

    [TestMethod]
    [DataRow("", "85")]
    [DataRow("abc", "85")]
    [DataRow("0", "85")]
    [DataRow("70", "101")]
    [DataRow("-5", "85")]
    [DataRow("+70", "85")]
    [DataRow("70.5", "85")]
    [DataRow("1,000", "85")]
    public void ThresholdsMustBeWholeNumbersFromOneToHundred(string orange, string red)
    {
        var form = Form(orange, red, "90");

        Assert.IsNull(form.TryRead());
        Assert.AreEqual("門檻必須是 1～100 的整數", form.Error);
    }

    [TestMethod]
    [DataRow("85", "85")]
    [DataRow("90", "85")]
    public void OrangeMustBeBelowRed(string orange, string red)
    {
        var form = Form(orange, red, "90");

        Assert.IsNull(form.TryRead());
        Assert.AreEqual("橘色門檻必須小於紅色門檻", form.Error);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("3651")]
    [DataRow("1.5")]
    [DataRow("九十")]
    public void RetentionMustBeWholeDaysFromOneTo3650(string retention)
    {
        var form = Form("70", "85", retention);

        Assert.IsNull(form.TryRead());
        Assert.AreEqual("保留天數必須是 1～3650 的整數", form.Error);
    }

    [TestMethod]
    public void AValidReadClearsAnEarlierError()
    {
        var form = Form("90", "85", "90");
        form.TryRead();

        form.OrangeText = "70";

        Assert.IsNotNull(form.TryRead());
        Assert.IsNull(form.Error);
    }

    [TestMethod]
    public void ChangesAreAnnouncedToTheWindow()
    {
        var form = new SettingsForm(AppSettings.Default);
        var changed = new List<string?>();
        form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        form.OrangeText = "60";
        form.AutoStart = true;
        form.ShowError("data 資料夾無法寫入，設定沒有儲存");

        CollectionAssert.AreEqual(new[] { "OrangeText", "AutoStart", "Error" }, changed);
        Assert.AreEqual("data 資料夾無法寫入，設定沒有儲存", form.Error);
    }
}

using AIUsageHub;

public class NotchShortcutTests
{
    [Fact] public void TwoQuickTapsToggleOnce()
    {
        var gesture = new DoubleAltGesture();
        Assert.False(gesture.Process(0xA4, true, 0));
        Assert.False(gesture.Process(0xA4, false, 70));
        Assert.False(gesture.Process(0xA4, true, 160));
        Assert.True(gesture.Process(0xA4, false, 220));
        Assert.False(gesture.Process(0xA4, true, 300));
        Assert.False(gesture.Process(0xA4, false, 360));
    }
    [Theory]
    [InlineData(0x09)] [InlineData(0x11)] [InlineData(0x10)] [InlineData(0x41)]
    public void ChordsDoNotToggle(int other)
    {
        var gesture = new DoubleAltGesture();
        gesture.Process(0xA4, true, 0); gesture.Process(0xA4, false, 50);
        gesture.Process(0xA4, true, 100); gesture.Process(other, true, 120);
        gesture.Process(other, false, 150);
        Assert.False(gesture.Process(0xA4, false, 170));
    }
    [Fact] public void HeldOrSlowAltDoesNotToggle()
    {
        var gesture = new DoubleAltGesture();
        gesture.Process(0xA4, true, 0); gesture.Process(0xA4, true, 100);
        Assert.False(gesture.Process(0xA4, false, 500));
        gesture.Process(0xA4, true, 600);
        Assert.False(gesture.Process(0xA4, false, 650));
        gesture.Process(0xA4, true, 1100);
        Assert.False(gesture.Process(0xA4, false, 1150));
    }
    [Fact] public void ModifierAlreadyHeldDoesNotToggle()
    {
        var gesture = new DoubleAltGesture();
        gesture.Process(0x11, true, 0);
        gesture.Process(0xA4, true, 50); gesture.Process(0xA4, false, 100);
        gesture.Process(0xA4, true, 180);
        Assert.False(gesture.Process(0xA4, false, 230));
    }
}

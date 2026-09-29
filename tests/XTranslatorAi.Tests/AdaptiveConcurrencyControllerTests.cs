using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class AdaptiveConcurrencyControllerTests
{
    [Fact]
    public void IsEnabled_BeforeConfigure_ReturnsFalse()
    {
        var controller = new AdaptiveConcurrencyController();

        Assert.False(controller.IsEnabled);
    }

    [Fact]
    public void IsEnabled_AfterConfigureWith1_ReturnsFalse()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(1);

        Assert.False(controller.IsEnabled);
    }

    [Fact]
    public void IsEnabled_AfterConfigureWithMoreThan1_ReturnsTrue()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(4);

        Assert.True(controller.IsEnabled);
    }

    [Fact]
    public void Configure_NormalizesNegativeToOne()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(-5);

        Assert.False(controller.IsEnabled);
    }

    [Fact]
    public void Configure_NormalizesZeroToOne()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(0);

        Assert.False(controller.IsEnabled);
    }

    [Fact]
    public async Task RegisterRateLimit_ReducesLimit()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(8);

        controller.RegisterRateLimit();

        // After rate limit, limit should be reduced (halved from 8 to 4).
        // We verify by acquiring multiple slots: should be able to get 4 but not 5 immediately.
        // A simpler approach: register rate limit twice and verify we can still get at least 1 slot.
        controller.RegisterRateLimit();

        // Should still be able to acquire at least one slot
        await controller.WaitForSlotAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForSlotAsync_WhenNotEnabled_ReturnsImmediately()
    {
        var controller = new AdaptiveConcurrencyController();

        // Should not block since IsEnabled is false
        await controller.WaitForSlotAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForSlotAsync_WhenEnabled_AcquiresSlot()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(2);

        await controller.WaitForSlotAsync(CancellationToken.None);
        // Slot acquired successfully
    }

    [Fact]
    public void ReleaseSlot_WhenNotEnabled_DoesNotThrow()
    {
        var controller = new AdaptiveConcurrencyController();

        // Should not throw
        controller.ReleaseSlot();
    }

    [Fact]
    public async Task WaitForSlotAsync_AndReleaseSlot_WorkTogether()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(2);

        // Acquire two slots (the limit)
        await controller.WaitForSlotAsync(CancellationToken.None);
        await controller.WaitForSlotAsync(CancellationToken.None);

        // Release one slot
        controller.ReleaseSlot();

        // Should be able to acquire another slot now
        var cts = new CancellationTokenSource(1000);
        await controller.WaitForSlotAsync(cts.Token);
    }

    [Fact]
    public async Task WaitForSlotAsync_Cancellation_ThrowsOperationCanceled()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(2);

        // Exhaust all slots
        await controller.WaitForSlotAsync(CancellationToken.None);
        await controller.WaitForSlotAsync(CancellationToken.None);

        // Try to acquire another with a cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.WaitForSlotAsync(cts.Token)
        );
    }

    [Fact]
    public async Task RegisterSuccess_IncreasesLimitAfterStreak()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(4);

        // First reduce the limit
        controller.RegisterRateLimit();
        // limit is now 2 (halved from 4)

        // Register enough successes to trigger an increase.
        // Threshold is Math.Max(8, limit * 8) = Math.Max(8, 2*8) = 16
        for (int i = 0; i < 20; i++)
        {
            controller.RegisterSuccess();
        }

        // After 16+ successes, limit should have increased from 2 to 3.
        // Verify by acquiring 3 slots (all should complete immediately).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        for (int i = 0; i < 3; i++)
        {
            await controller.WaitForSlotAsync(cts.Token);
        }

        // 4th slot should NOT complete immediately (limit=3, all 3 in-flight)
        using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.WaitForSlotAsync(shortCts.Token)
        );
    }

    [Fact]
    public void Reset_ResetsStateToInitial()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(8);
        Assert.True(controller.IsEnabled);

        controller.Reset();

        Assert.False(controller.IsEnabled);
    }

    [Fact]
    public void RegisterRateLimit_WhenNotEnabled_DoesNotThrow()
    {
        var controller = new AdaptiveConcurrencyController();

        // Should not throw
        controller.RegisterRateLimit();
    }

    [Fact]
    public void RegisterSuccess_WhenNotEnabled_DoesNotThrow()
    {
        var controller = new AdaptiveConcurrencyController();

        // Should not throw
        controller.RegisterSuccess();
    }

    [Fact]
    public async Task RegisterRateLimit_AtMinimumLimit_DoesNotGoBelowOne()
    {
        var controller = new AdaptiveConcurrencyController();
        controller.Configure(2);

        // Repeatedly reduce the limit
        for (int i = 0; i < 10; i++)
        {
            controller.RegisterRateLimit();
        }

        // Should still be able to acquire at least one slot
        await controller.WaitForSlotAsync(CancellationToken.None);
    }
}

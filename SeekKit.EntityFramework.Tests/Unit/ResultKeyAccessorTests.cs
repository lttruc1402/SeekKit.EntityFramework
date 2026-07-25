using SeekKit.Core.Helpers;

namespace SeekKit.EntityFramework.Tests.Unit;

public sealed class ResultKeyAccessorTests
{
    private sealed class SimpleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class InnerDto
    {
        public DateTime CreatedAt { get; set; }
    }

    private sealed class NestedDto
    {
        public InnerDto Inner { get; set; } = new();
    }

    [Fact]
    public void GetAccessor_SimpleProperty_ReadsValue()
    {
        var accessor = ResultKeyAccessor.GetAccessor<SimpleDto>("Id");
        var dto = new SimpleDto { Id = 42, Name = "x" };

        Assert.Equal(42, accessor(dto));
    }

    [Fact]
    public void GetAccessor_NestedPropertyPath_ReadsValue()
    {
        var accessor = ResultKeyAccessor.GetAccessor<NestedDto>("Inner.CreatedAt");
        var dto = new NestedDto { Inner = new InnerDto { CreatedAt = new DateTime(2026, 1, 1) } };

        Assert.Equal(new DateTime(2026, 1, 1), accessor(dto));
    }

    [Fact]
    public void GetAccessor_MissingProperty_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResultKeyAccessor.GetAccessor<SimpleDto>("DoesNotExist"));

        Assert.Contains("DoesNotExist", ex.Message);
        Assert.Contains(nameof(SimpleDto), ex.Message);
    }

    [Fact]
    public void GetAccessor_SamePathAndType_ReturnsCachedDelegateInstance()
    {
        var first  = ResultKeyAccessor.GetAccessor<SimpleDto>("Id");
        var second = ResultKeyAccessor.GetAccessor<SimpleDto>("Id");

        Assert.Same(first, second);
    }
}

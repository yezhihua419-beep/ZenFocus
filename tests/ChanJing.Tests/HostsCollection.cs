using Xunit;

namespace ChanJing.Tests;

/// <summary>
/// 涉及 hosts 静态状态的测试归入同一集合，强制串行，
/// 避免静态 HostsPathOverride 在并行测试类间互相覆盖。
/// </summary>
[CollectionDefinition("Hosts", DisableParallelization = true)]
public class HostsCollection
{
}

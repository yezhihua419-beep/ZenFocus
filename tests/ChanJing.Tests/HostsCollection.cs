using Xunit;

namespace ChanJing.Tests;

/// <summary>
/// 涉及 hosts / ResolveLocale 静态状态的测试归入同一集合，强制串行，
/// 避免 HostsPathOverride、PreApplyPathOverride、CatalogPathOverride、名单语言在并行类间互相覆盖。
/// </summary>
[CollectionDefinition("Hosts", DisableParallelization = true)]
public class HostsCollection
{
}

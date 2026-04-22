using System;

namespace ShadowLink.Infrastructure.Session;

internal readonly record struct ConnectionCandidate(String HostOrAddress);

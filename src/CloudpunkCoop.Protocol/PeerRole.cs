namespace CloudpunkCoop.Protocol
{
    /// <summary>Role assigned by the host in <see cref="WelcomeMessage"/>.</summary>
    public enum PeerRole : byte
    {
        Host = 0,
        Guest = 1,
    }
}

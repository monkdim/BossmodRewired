using System.Globalization;
using System.Security.Cryptography;

namespace BossMod;

/// <summary>
/// How a player is named in anything that leaves this machine.
///
/// A recording on your own disk may as well carry real names: it is your data about your own evening. An
/// export is different, because the point of an export is to hand it to somebody. So the sanitising happens
/// here, at the moment a participant is written into a report, rather than at recording time. Recording stays
/// complete and roles keep resolving from the real content IDs the log already holds.
///
/// What replaces a name is a short hash of the content ID with a salt. That gives the one property a report
/// actually needs from identity: the same person reads as the same handle everywhere in the file, so "this
/// one stood here and that one stood there" survives, while the handle means nothing to anybody who does not
/// hold the salt.
///
/// The salt is what decides how far that reach goes. A random one, generated per install, keeps handles
/// meaningful only inside one person's own files. A salt shared by a group makes the same player read
/// identically across all of their recordings, which is what lets three people's logs of one pull be pooled.
/// Neither can be turned back into an account: a content ID is sixty-four bits, but it is not a secret to
/// anybody with the salt, and the salt is the thing you choose who to give.
/// </summary>
public sealed class SharingConfig : ConfigNode
{
    [PropertyDisplay("Salt used to disguise players in exports", tooltip: "Anything you like. Everyone who shares this exact text will see the same player under the same handle in their exports, which is what lets several recordings of one pull be pooled. Leave it alone and yours stays private to you.")]
    public string Salt = "";

    /// <summary>
    /// The salt, generating one the first time it is asked for.
    ///
    /// Generated rather than left empty on purpose: an empty salt would hash every install identically and
    /// quietly hand out handles that link across people who never agreed to that.
    /// </summary>
    public string EffectiveSalt()
    {
        if (Salt.Length == 0)
        {
            Salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            Modified.Fire();
        }

        return Salt;
    }
}

/// <summary>Turns a participant into something safe to write into a file somebody else will read.</summary>
public static class SharedIdentity
{
    // Long enough that two players in one recording will not collide, short enough to read in a table. Eight
    // hex characters over a party of eight is not a coincidence anybody will meet.
    private const int Digits = 8;

    private static string _saltCache = "";
    private static readonly Dictionary<ulong, string> _handles = [];

    /// <summary>
    /// A stable handle for one content ID, meaningless without the salt.
    ///
    /// A content ID of zero means the game never told us who this was, which is every player outside your own
    /// party. They get no handle at all rather than a shared one, since pretending they are all the same
    /// person would be worse than admitting we cannot tell them apart.
    /// </summary>
    public static string Handle(ulong contentID)
    {
        if (contentID == 0)
        {
            return "someone";
        }

        // Compared rather than cached once, so editing the salt to match a group's takes effect on the next
        // export instead of the next launch. Everything hashed under the old one has to go with it.
        var salt = Service.Config.Get<SharingConfig>().EffectiveSalt();
        if (_saltCache != salt)
        {
            _saltCache = salt;
            _handles.Clear();
        }

        if (_handles.TryGetValue(contentID, out var cached))
        {
            return cached;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes($"{salt}:{contentID.ToString(CultureInfo.InvariantCulture)}");
        var handle = Convert.ToHexString(SHA256.HashData(bytes))[..Digits].ToLowerInvariant();
        _handles[contentID] = handle;
        return handle;
    }
}

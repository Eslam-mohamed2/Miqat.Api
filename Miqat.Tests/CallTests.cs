using System.Security.Cryptography;
using System.Text;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>Who may be on a call, which tab talks to which, and what a call leaves in the chat.</summary>
public class CallTests
{
    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Cy = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static CallSession Call(Guid caller, Guid callee, DateTime at, string tab = "tab-caller") => new()
    {
        ConversationId = Guid.NewGuid(), CallerId = caller, CalleeId = callee, CallerConnectionId = tab, StartedAt = at
    };

    [Fact]
    public void A_person_is_on_one_call_at_a_time()
    {
        var registry = new CallRegistry();

        Assert.True(registry.TryStart(Call(Ann, Ben, Noon), out _));

        Assert.False(registry.TryStart(Call(Cy, Ben, Noon.AddSeconds(5)), out var busy));
        Assert.Equal(Ben, busy);
        Assert.False(registry.TryStart(Call(Ann, Cy, Noon.AddSeconds(5)), out busy));
        Assert.Equal(Ann, busy);

        Assert.True(registry.IsBusy(Ann, Noon.AddSeconds(5)));
        Assert.False(registry.IsBusy(Cy, Noon.AddSeconds(5)));
    }

    [Fact]
    public void Nobody_can_ring_without_end()
    {
        var registry = new CallRegistry();

        for (var i = 0; i < CallRegistry.MaxAttemptsPerMinute; i++)
            Assert.True(registry.TryCountAttempt(Ann, Noon.AddSeconds(i)));

        Assert.False(registry.TryCountAttempt(Ann, Noon.AddSeconds(30)));
        // Someone else is not affected, and a minute later Ann may call again.
        Assert.True(registry.TryCountAttempt(Ben, Noon.AddSeconds(30)));
        Assert.True(registry.TryCountAttempt(Ann, Noon.AddSeconds(61)));
    }

    [Fact]
    public void A_call_that_rang_out_no_longer_blocks_anyone()
    {
        var registry = new CallRegistry();
        registry.TryStart(Call(Ann, Ben, Noon), out _);
        var later = Noon + CallRegistry.RingLimit + TimeSpan.FromSeconds(1);

        Assert.False(registry.IsBusy(Ben, later));
        Assert.True(registry.TryStart(Call(Cy, Ben, later), out _));
    }

    [Fact]
    public void An_answered_call_keeps_both_busy_however_long_it_lasts()
    {
        var registry = new CallRegistry();
        var call = Call(Ann, Ben, Noon);
        registry.TryStart(call, out _);
        registry.TryAnswer(call.Id, Ben, "tab-ben", Noon.AddSeconds(4));

        Assert.True(registry.IsBusy(Ann, Noon.AddHours(2)));
        Assert.False(registry.TryStart(Call(Cy, Ben, Noon.AddHours(2)), out _));
    }

    [Fact]
    public void Only_the_friend_being_called_can_answer_and_only_once()
    {
        var registry = new CallRegistry();
        var call = Call(Ann, Ben, Noon);
        registry.TryStart(call, out _);

        Assert.False(registry.TryAnswer(call.Id, Cy, "tab-cy", Noon));
        Assert.False(registry.TryAnswer(call.Id, Ann, "tab-caller", Noon));
        Assert.True(registry.TryAnswer(call.Id, Ben, "tab-ben", Noon));
        Assert.False(registry.TryAnswer(call.Id, Ben, "tab-ben-2", Noon));
        Assert.Equal("tab-ben", call.CalleeConnectionId);
    }

    [Fact]
    public void Set_up_messages_only_pass_between_the_two_tabs_of_an_answered_call()
    {
        var registry = new CallRegistry();
        var call = Call(Ann, Ben, Noon);
        registry.TryStart(call, out _);

        // Still ringing: nothing passes yet.
        Assert.Null(call.PeerOf("tab-caller"));

        registry.TryAnswer(call.Id, Ben, "tab-ben", Noon);

        Assert.Equal("tab-ben", call.PeerOf("tab-caller"));
        Assert.Equal("tab-caller", call.PeerOf("tab-ben"));
        Assert.Null(call.PeerOf("tab-ben-2"));
        Assert.Null(call.PeerOf("tab-of-a-stranger"));
    }

    [Fact]
    public void A_call_ends_once_and_is_found_by_either_tab_until_then()
    {
        var registry = new CallRegistry();
        var call = Call(Ann, Ben, Noon);
        registry.TryStart(call, out _);
        registry.TryAnswer(call.Id, Ben, "tab-ben", Noon);

        Assert.Same(call, registry.FindByConnection("tab-caller"));
        Assert.Same(call, registry.FindByConnection("tab-ben"));
        Assert.Null(registry.FindByConnection("tab-ben-2"));

        Assert.Same(call, registry.Remove(call.Id));
        Assert.Null(registry.Remove(call.Id));
        Assert.False(registry.IsBusy(Ann, Noon));
    }

    [Fact]
    public void A_call_is_recorded_in_the_chat_as_a_message_from_the_caller()
    {
        var done = ChatMessage.ForCall(Guid.NewGuid(), Ann, video: true, CallOutcome.Completed, 135);
        var missed = ChatMessage.ForCall(Guid.NewGuid(), Ann, video: false, CallOutcome.Missed, 40);

        Assert.True(done.IsCall);
        Assert.Equal(Ann, done.SenderId);
        Assert.Equal(135, done.CallSeconds);
        Assert.True(done.CallVideo);
        Assert.Equal(string.Empty, done.Body);

        // Only a call that was answered has a length.
        Assert.Equal(0, missed.CallSeconds);
        Assert.Throws<InvalidOperationException>(() => done.Edit("we never spoke"));
        Assert.False(new ChatMessage(Guid.NewGuid(), Ann, "hi").IsCall);
    }

    [Fact]
    public void A_relay_password_is_tied_to_the_user_and_expires()
    {
        var turn = new TurnSettings { Urls = { "turn:turn.example.org:3478" }, Secret = "shared-secret", TtlSeconds = 600 };

        var access = CallService.TemporaryTurnAccess(turn, Ann, Noon);

        var expires = new DateTimeOffset(Noon.AddSeconds(600)).ToUnixTimeSeconds();
        Assert.Equal($"{expires}:{Ann:N}", access.Username);

        // What coturn computes on its side with the same secret.
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("shared-secret"));
        Assert.Equal(Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(access.Username!))), access.Credential);
        Assert.Equal(turn.Urls, access.Urls);
        Assert.NotEqual(access.Credential, CallService.TemporaryTurnAccess(turn, Ben, Noon).Credential);
    }

    [Fact]
    public void A_relay_answer_as_a_list_is_read()
    {
        // The form with the key inside the address (Metered).
        var servers = RelayAnswer.Parse("""
            [ { "urls": "stun:stun.relay.example:80" },
              { "urls": "turn:global.relay.example:443?transport=tcp", "username": "u1", "credential": "p1" } ]
            """);

        Assert.Equal(2, servers.Count);
        Assert.Equal(new[] { "stun:stun.relay.example:80" }, servers[0].Urls);
        Assert.Null(servers[0].Username);
        Assert.Equal(new[] { "turn:global.relay.example:443?transport=tcp" }, servers[1].Urls);
        Assert.Equal("u1", servers[1].Username);
        Assert.Equal("p1", servers[1].Credential);
    }

    [Fact]
    public void A_relay_answer_wrapped_in_iceServers_is_read_and_port_53_is_left_out()
    {
        // The form of Cloudflare. Browsers refuse port 53, so those addresses only slow a call down.
        var servers = RelayAnswer.Parse("""
            { "iceServers": [
                { "urls": [ "stun:stun.example.com:3478", "stun:stun.example.com:53" ] },
                { "urls": [ "turn:turn.example.com:3478?transport=udp", "turn:turn.example.com:53?transport=udp",
                            "turns:turn.example.com:443?transport=tcp" ],
                  "username": "name", "credential": "secret" } ] }
            """);

        Assert.Equal(2, servers.Count);
        Assert.Equal(new[] { "stun:stun.example.com:3478" }, servers[0].Urls);
        Assert.Equal(new[] { "turn:turn.example.com:3478?transport=udp", "turns:turn.example.com:443?transport=tcp" }, servers[1].Urls);
        Assert.Equal("name", servers[1].Username);
    }

    [Fact]
    public void A_relay_answer_with_one_server_object_is_read()
    {
        var servers = RelayAnswer.Parse("""
            { "iceServers": { "urls": [ "turn:turn.example.com:3478" ], "username": "n", "credential": "c" } }
            """);

        var only = Assert.Single(servers);
        Assert.Equal("c", only.Credential);
    }

    [Theory]
    [InlineData("""{ "error": "bad key" }""")]
    [InlineData("""[ { "urls": "https://not-a-relay.example" }, { "username": "x" }, 5 ]""")]
    [InlineData("""{ "iceServers": [ { "urls": [ "turn:turn.example.com:53" ] } ] }""")]
    public void A_relay_answer_with_nothing_usable_gives_no_servers(string json)
    {
        Assert.Empty(RelayAnswer.Parse(json));
    }
}

<p align="center"> <img width="867" height="234" alt="WWR" src="https://github.com/user-attachments/assets/729748d2-bd43-4fea-976c-db126ff757a7" /> </p>


# War Without Reason

> *PvP has consequences. Even in safe zones.*

A server-side combat log system for Vintage Story that closes the door on one of PvP's oldest tricks — logging out mid-fight to dodge death. Plus a per-claim flag system so server owners can decide where blood is spilled and where it isn't.

No external mods required. Runs alongside SafeZone and DisablePvPInsideClaims if you happen to use them.

---

## The idea

You swing at someone. They swing back. For the next 30 seconds (or however long you configure), **both of you are in combat** — and combat has rules.

- You can't hide in a claim anymore. If the fight started outside, the fight continues inside.
- If you log out to escape, the server remembers. Next time you log in, you die.
- On-screen timer keeps you honest about how much longer you have to survive.
- Claim owners decide: is your territory a sanctuary, or an arena?

---

## What it actually does

### 🥊 Combat tracking

When one player hits another, both get flagged as "in combat" for a configurable duration (30 seconds by default).

- Every new hit **extends** the timer back to full. No refreshing, no shortening.
- Arrows, thrown spears, melee — all count. If `GetCauseEntity()` says it was a player, it's PvP.
- Mobs, falls, fire, hunger — ignored. Only player-vs-player triggers combat mode.
- Notifications only fire on **state change** — the first hit and the moment the timer expires. No spam.

### ⏱️ HUD timer

A timer on screen shows exactly how many seconds you have left.

- Server sends one packet per second over a dedicated network channel.
- Client renders it in the center-top of the screen.
- Updates in real time, hides itself when combat ends.
- Supports RU and EN out of the box.

### 💀 Dead Hand (combat logout punishment)

Log out during a fight? The server doesn't forget.

- Your UID is written to `WWR_PendingDeaths.json` and survives server restarts.
- Next time you log in, you die — cleanly, with a `Suicide` damage source so the game doesn't misattribute the kill to your opponent.
- All online players get a chat notification.
- Can be disabled with `KillOnCombatLogout: false` if your server doesn't want this.

### 🛡️ Claim protection with teeth

Every claim can independently allow or forbid PvP and PvE.

- By default, all claims block PvP and PvE from outside players.
- Combat override: if **both** fighters are in combat, damage passes through any claim — that's how you chase someone down.
- A peaceful player inside a claim cannot be attacked by someone who is in combat — unless `AllowOwnerDefenseInClaims` or `AllowOpenDefenseInClaims` is enabled.

### ⚔️ Defense policies

Three modes, configurable per server:

| Mode | What it does |
|---|---|
| **Chase** (always on) | Both players in combat → damage passes in claims |
| **Owner Defense** | A claim owner may strike anyone in combat who enters their territory |
| **Open Defense** | Any player inside any claim may strike anyone in combat (⚠️ enables third-party ganking) |

---

## Commands
/claimflag pvp on|off|default — Allow, forbid, or inherit PvP setting for this claim  
/claimflag pve on|off|default — Same, but for mob damage  
/claimflag status — Show current flags  


All commands run in chat. Only the claim owner (or a player with build rights) can change flags. Admins with `root` can always override.

`default` resets the flag to `null`, so the claim inherits the global config value.

---

## Configuration

Two JSON files live in `ModConfig/`:

### `WarWithoutReason.json`

```json
{
  "CombatDurationSeconds": 30,
  "SendChatMessages": true,
  "ShowScreenMessages": true,
  "KillOnCombatLogout": true,
  "EnableClaimProtection": true,
  "PreventPvPInClaims": true,
  "PreventPvEInClaims": false,
  "AllowOwnerDefenseInClaims": true,
  "AllowOpenDefenseInClaims": false,
  "AllowPlayersToManageClaimFlags": true
}
```

### `WWR_ClaimFlags.json`
Managed automatically by /claimflag. Don't edit by hand unless you know what you're doing.

```json
{
  "Flags": {
    "511982,115,512107": {
      "AllowPvP": true,
      "AllowPvE": null
    }
  }
}
```
Keys are claim center coordinates — unique because claims can't overlap.

## Installation
### Server:

- Drop the ZIP into your Mods folder.
- Restart the server.
- Configure WarWithoutReason.json to taste.

### Client:
Required if you want the on-screen combat timer. Install the same ZIP, no config needed.  
Without the client mod, players still get chat notifications and combat tracking — they just don't see the timer.

## Requirements
 * Global PvP must be enabled on the server (AllowPvP: true). This mod tracks PvP — it doesn't enable it.
 * Only player-vs-player damage triggers combat. Fall damage, mobs, and NPCs don't.
 * Vintage Story 1.21.0+ / 1.22.x

## Compatibility
* SafeZone: if installed, built-in claim protection disables itself automatically. SafeZone handles the blocking, our mod still provides the timer, notification, and logout punishment.
* DisablePvPInsideClaims: same approach — our protection steps aside, we override the combat exception so chasing still works.
* Everything else: no known conflicts.

## Known limitations
* Traps don't count as PvP. Lava, cactus, fall damage inside a claim go through regardless. This is up to server admins to police.
* Timers don't survive server restarts. If the server crashes mid-fight, everyone is free. The Dead Hand list does survive, so logouts are still punished.
* Logouts during server shutdown aren't punished. Only clean disconnects and disconnections while the server is running.
* Third-party ganking is possible if `AllowOpenDefenseInClaims` is enabled. Enable at your own risk.

## What's next
Ideas parked for future versions:
* Different combat durations for PvP vs PvE
* Blocking teleport commands (/home, /spawn) during combat
* Admin commands (/combat status <player>, /combat clear <player>)
* Battle logging to a file for admin review
* Persistent reputation tracking
* None of these are promised. They're ideas.

## License
MIT — do whatever you want, but if you make something cool, let me know. Have a fun =)

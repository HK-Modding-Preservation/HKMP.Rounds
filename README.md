# HKMP.Rounds

## Chat Commands
* `/start`, `/round` — **starts a new round**. *Available only for the admin/host.*
* `/rd`,`/RD`, `/рд`, `/РД`, `/ready` — marks a player as **Ready** (when auto-start is enabled).
* `/round end` — **manually ends** the current round.
* `/round wins <number|off>` — sets or disables the **match win target** (value from `1` to `1000`; can only be changed *before* the round starts).
* `/round mp <0-198>` — changes the **amount of MP (SOUL)** restored at the beginning of a round.
* `/stats` — displays the current **win statistics** for all players.
* `/resetstats` — completely **resets win statistics**. *Available only for the admin.*
* `/rd qty <0-1000>` — sets the required number of ready players to start and end a round (0 = all players on the host). *Available only for the administrator.*
---

## Gameplay Features & Phases

### Auto-Start
* When auto-start is enabled, a new round begins automatically once **all connected players** type `/rd` (or its aliases).
* The server dynamically **displays current readiness status** in the chat.
* As soon as everyone is ready, the addon starts the round and announces its beginning.
* The auto-start configuration **persists across hosts**.

### Before the Round Starts:
1. **All exits** from the current room (arena) are blocked.
2. The defeated player's **shade is automatically destroyed** to prevent arena clutter.
3. Standard room **enemies (mobs) are completely disabled**.
4. Players are given the configured amount of **mana (SOUL)**.
5. The host's **last bench position is recorded** for the post-round teleportation.

### At the End of the Round:
* All players are instantly teleported back to the **last tracked bench**.
* The remaining **HP (masks) and MP (SOUL)** of the **survivors** are displayed in the server chat.
* *With timer integration enabled:* the `/start`, `/round`, `/rd`, `/рд`, `/ready` commands automatically **trigger the timer countdown**. Upon its expiration, all players are forcefully teleported back to the bench, and the survivors' HP/MP stats are displayed.

---

## HKMP.Timer Integration
* A round can end by a standard PvP victory condition, a timer timeout, or manually via `/round end`.
* **In stopwatch mode:** the commands above start its countdown, and the **exact duration** of the combat is displayed in the chat once the round concludes.

### Mod Menu Options (In-Game Menu)
The in-game mod menu allows the host to toggle the following settings:
* [x] **Auto-start** rounds
* [x] **Disable mobs** in the arena
* [x] **Disable player shade** spawning
* [x] **Timer integration**

---

## Community & Credits
`HKMP.Rounds` is a proud part of the official **Hollow Knight PvP-Events Community**.  
Announcements, tournaments, custom PvP modes, and additional information can be found here:
* **[Events HK on Telegram](https://t.me)** *(replace with your actual link)*

> *A portion of this project's code was optimized and generated using AI. The final codebase is fully maintained, integrated, and thoroughly tested by the project author.*

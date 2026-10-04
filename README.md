<p align="center"> <img width="867" height="234" alt="WWR" src="https://github.com/user-attachments/assets/729748d2-bd43-4fea-976c-db126ff757a7" /> </p>


## War Without Reason
A mod that implements the timer logic of the player's "in battle" status. This mod relies on the already created SafeZone (although in the future, perhaps, this functionality will be in full swing).

### How this works?
In order for this mod to work, PvP is required worldwide. When a "predator" player attacks a "victim" player, both have the status of "in combat". After that, the timer starts for both, and if the "predator" did not deal damage, and the "victim" did not receive it, in this case both lose their "in battle" status. Important: only damage from the player is taken into account, but not from the external environment (falls, animal attacks, NPCs, etc...).

### What does SafeZone have to do with it?
SafeZone allows players to allow or prohibit PvP/PvE in their claim, and also disables PvP/PvE on some blocks (roads, for example) by default. Thus, a player outside the "safe zone" of the claim cannot attack a player in the "safe zone".  
The mod I created introduces a new variable, "in combat" status, eliminating the dishonesty of the game: if the "victim" is attacked and goes into the "safe zone", the "predator" will no longer be able to attack it. The status ignores the "safe zones" until the timer expires, so the victim will not be able to sit in it.

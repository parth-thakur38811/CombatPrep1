using System;
using UnityEngine;
using CombatPrep.Player;
using CombatPrep.UI;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// All five guns, carried at once: keys 1 to 5 take out the carbine, SMG, DMR, pistol or
    /// shotgun (WeaponLibrary order). The one picked in the menu is simply the one you start
    /// with.
    ///
    /// Each gun is its own Weapon on its own holder, and only the one in hand is active, so
    /// exactly one reads the trigger. A gun put away keeps its magazine but drops a reload in
    /// progress, and the gun taken out has to come up before it can fire (DrawTime).
    ///
    /// A grenade holsters the gun in hand (SetHolstered); pressing a number while a grenade is
    /// out puts the grenade back and brings up that gun.
    /// </summary>
    public class WeaponLoadout : MonoBehaviour
    {
        public GrenadeThrower Thrower;

        Weapon[] _weapons;
        string[] _names;
        int _index;
        bool _holstered;

        public Weapon[] Weapons => _weapons;
        /// <summary>The gun in hand, as a WeaponLibrary index.</summary>
        public int Index => _index;
        public Weapon Current => _weapons[_index];

        /// <summary>Raised when a different gun is taken out, with its WeaponLibrary index.</summary>
        public event Action<int> Switched;

        /// <param name="weapons">One per WeaponLibrary entry, in that order, all switched off.</param>
        public void Init(Weapon[] weapons, int start)
        {
            _weapons = weapons;
            _names = new string[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) _names[i] = weapons[i].Def.DisplayName;

            _index = Mathf.Clamp(start, 0, weapons.Length - 1);
            TakeOut(Current);
            Hud.I.ShowWeaponSlots(_names, _index);
        }

        void Update()
        {
            int key = GameInput.I != null ? GameInput.I.WeaponKeyPress : -1;
            if (key < 0 || key >= _weapons.Length) return;

            Select(key);
            // With a grenade out, the grenade goes back and it's this gun that comes up.
            if (Thrower != null && Thrower.Equipped) Thrower.Holster();
            Hud.I.ShowWeaponSlots(_names, _index);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _weapons.Length || index == _index) return;

            Current.gameObject.SetActive(false);
            _index = index;
            if (!_holstered) TakeOut(Current);
            Switched?.Invoke(index);
        }

        /// <summary>Puts the gun in hand away (a grenade is out), or brings it back up.</summary>
        public void SetHolstered(bool holstered)
        {
            if (_holstered == holstered) return;
            _holstered = holstered;
            if (holstered) Current.gameObject.SetActive(false);
            else TakeOut(Current);
        }

        static void TakeOut(Weapon w)
        {
            w.gameObject.SetActive(true);
            w.Draw();
        }

        /// <summary>Every gun back to a full magazine and reserve - a respawn.</summary>
        public void ResetAmmo()
        {
            foreach (var w in _weapons) w.ResetAmmo();
        }

        /// <summary>
        /// Off while dead: the gun in hand stops working and the number keys do nothing. Back on,
        /// the gun comes up again.
        /// </summary>
        public void SetControls(bool on)
        {
            enabled = on;
            Current.Active = on;
            if (on && !_holstered) Current.Draw();
        }
    }
}

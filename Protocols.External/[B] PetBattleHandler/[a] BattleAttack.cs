using Server.Reawakened.Network.Extensions;
using Server.Reawakened.Network.Protocols;
using Server.Reawakened.Players.Models.Pets;

namespace Protocols.External._B__PetBattleHandler;
public class BattleAttack : ExternalProtocol
{
    public override string ProtocolName => "Ba";

    public override void Run(string[] message)
    {
        var attack = int.Parse(message[6]);
        var pet = int.Parse(message[7]);
        var target = int.Parse(message[8]);

        var model = Player.TempData.PetBattleModel;

        var randomChance = new Random().Next(100);
        if (randomChance > model.Pets[pet].accuracy)
            Player.SendXt("Bm", pet, target);

        else
            SendAbilityType(attack, pet, target, model);

        Player.SendXt("BT", pet >= 3 ? "1" : "0");
    }

    private void SendAbilityType(int attack, int pet, int target, PetBattleModel model)
    {
        var battlePet = model.Pets[pet];
        var ability = battlePet.abilities.FirstOrDefault(x => x.index == attack);

        //Attacking.
        if (ability.damageValue > 0)
        {
            var isAOEAttack = target == -1;

            if (isAOEAttack)
                //[Fix later] Opponent AOE's will attack their own team.
                foreach (var enemyPet in model.Pets.Skip(3))
                {
                    target = model.Pets.IndexOf(enemyPet);
                    DamagePet(ability, enemyPet, model);
                }

            else
                DamagePet(ability, model.Pets[target], model);
        }

        //Healing.
        if (ability.healthValue > 0)
        {
            var targetPet = model.Pets[target];

            //if (targetPet.health + ability.healthValue > targetPet.maxHealth <-- CREATE MAX HEALTH)
            //ability.healthValue = targetPet.health + ability.healthValue - targetPet.maxHealth;

            Player.SendXt("BH", target, ability.healthValue, targetPet.health += ability.healthValue, "0");
        }

        //Shielding.
        if (ability.healthMitigation > 0)
            Player.SendXt("By", target, ability.durationRounds, ability.healthMitigation, ability.healthMitigation, pet, "0");

        if (ability.cooldownRounds > 0)
            CooldownPet(pet, ability.cooldownRounds);

        if (ability.durationRounds > 0)
            DamagePet(ability, model.Pets[target], model);
    }

    private void DamagePet(PetBattlePetsXML.PetBattlePetAbility petAbility, PetBattlePetsXML.PetBattlePet targetPet, PetBattleModel model)
    {
        targetPet.health -= petAbility.damageValue;

        if (targetPet.health < 0)
            targetPet.health = 0;

        var targetIndex = model.Pets.IndexOf(targetPet);

        Player.SendXt("Bd", targetIndex, petAbility.damageValue, targetPet.health, petAbility.durationRounds, "0");

        if (PetIsDead(targetPet))
            PetKO(targetIndex);
    }

    private void CooldownPet(int petIndex, int duration) =>
       Player.SendXt("BL", petIndex, petIndex, duration);

    private bool PetIsDead(PetBattlePetsXML.PetBattlePet pet) => pet.health <= 0;

    private void PetKO(int target) => Player.SendXt("Bk", target, "0");
}

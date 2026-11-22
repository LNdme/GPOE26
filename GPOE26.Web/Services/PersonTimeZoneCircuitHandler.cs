using GPOE26.UI.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace GPOE26.Web.Services;

/// <summary>
/// Lot B3 : chaque activité d'un circuit (clic, saisie, navigation, appel JavaScript) s'exécute dans le fuseau de la
/// personne connectée à ce circuit. Le fuseau, lu dans le profil au moment de chaque conversion, vaut pour la durée de l'activité seulement (<see cref="AppTime.Use"/>) :
/// deux personnes servies en même temps par ce serveur ne se le partagent jamais.
/// </summary>
public sealed class PersonTimeZoneCircuitHandler(SessionState session) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            using (AppTime.Use(() => AppTime.ZoneOf(session.Profile)))
                await next(context);
        };
}

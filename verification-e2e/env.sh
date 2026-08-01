# Valeurs reprises telles quelles de GPOE26.AppHost/AppHost.cs.
#
# Les deux services doivent signer et valider avec la MÊME clé : sinon tout répond 401
# et l'on cherche un problème de droits là où il y a un problème de configuration.
export Jwt__Key="votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits"
export Jwt__Issuer="GPOE2026"
export Jwt__Audience="GPOE2026Users"
export ASPNETCORE_ENVIRONMENT=Development
export PG="Host=localhost;Port=5432;Username=postgres;Password=postgres"

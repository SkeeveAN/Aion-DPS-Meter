-- 0018's DELETE silently matched nothing on production: it keyed off `server_catalog.name` =
-- 'AionRiftshade' (no space), but migration 0011 already renamed that row to 'Aion Riftshade' (with
-- a space) - the subquery returned NULL, `server_catalog_id = NULL` never matches, so all 25
-- instances stayed mapped (confirmed via GET /api/instances?serverCatalogId= against production
-- straight after deploying 0018 - the sort_order UPDATEs, which don't depend on this name, DID take
-- effect). Re-running the same DELETE here with the correct current name - safe to repeat even
-- where a row was already (correctly) unmapped, since DELETE on a non-matching row is a no-op.
DELETE FROM `server_catalog_instances`
WHERE `server_catalog_id` = (SELECT id FROM `server_catalog` WHERE name = 'Aion Riftshade')
  AND `instance_id` IN (
    SELECT id FROM `instances` WHERE name IN (
      'Aturam Sky Fortress',
      'Baruna Research Laboratory',
      'Beshmundirs Tempel',
      'Hall of Knowledge',
      'Jormungand''s Bridge',
      'Linkgate Foundry',
      'Lost Refuge',
      'Mantor',
      'Rentus-Basis',
      'Tahmes',
      'Tiamats Festung',
      'Stahlrose: Anlegestelle',
      'Stahlrose: Kabine',
      'Stahlrose: Deck',
      'Ophidan Bridge',
      'Danuar Sanctuary'
    )
  );

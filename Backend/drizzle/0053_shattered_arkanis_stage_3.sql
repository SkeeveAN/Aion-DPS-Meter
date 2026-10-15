-- Shattered Arkanis stage 3 carries the boss ids 2353xxx (not 2370xxx as guessed): the fights filed without a stage (mode empty) get it.
UPDATE `encounters` SET `mode` = '3'
  WHERE `mode` = '' AND `id` IN (
    SELECT `matched_encounter_id` FROM `uploads`
    WHERE `matched_encounter_id` IS NOT NULL AND CAST(json_extract(`raw_payload_json`, '$.bossNpcId') AS INTEGER) BETWEEN 2353000 AND 2353999
  );

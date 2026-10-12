-- S23: planned, private center campaigns. Linked to an actual pinned local-plan
-- revision, not to a fabricated national issuance or observed execution.
BEGIN;
CREATE SCHEMA IF NOT EXISTS campaigns;
REVOKE ALL ON SCHEMA campaigns FROM PUBLIC;
CREATE TABLE campaigns.center_campaigns (
 center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 campaign_id uuid NOT NULL,
 slug text NOT NULL CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug)<=80),
 draft_id uuid NOT NULL,
 pinned_plan_revision bigint NOT NULL CHECK(pinned_plan_revision>0),
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,campaign_id),
 UNIQUE(center_id,slug),
 FOREIGN KEY(center_id,draft_id,pinned_plan_revision)
   REFERENCES programs.center_plan_draft_revisions(center_id,draft_id,revision) ON DELETE RESTRICT
);
CREATE TABLE campaigns.center_campaign_revisions (
 center_id text NOT NULL,
 campaign_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
 goal_text text NOT NULL CHECK(length(btrim(goal_text)) BETWEEN 1 AND 500),
 audience_description text NOT NULL CHECK(length(btrim(audience_description)) BETWEEN 1 AND 240),
 planned_activities jsonb NOT NULL CHECK(jsonb_typeof(planned_activities)='array'
     AND jsonb_array_length(planned_activities) BETWEEN 1 AND 12),
 changed_by_subject text NOT NULL CHECK(length(btrim(changed_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,campaign_id,revision),
 FOREIGN KEY(center_id,campaign_id) REFERENCES campaigns.center_campaigns(center_id,campaign_id) ON DELETE RESTRICT
);
CREATE INDEX campaign_recent ON campaigns.center_campaign_revisions(center_id,campaign_id,revision DESC);
CREATE TRIGGER campaign_identity_immutable BEFORE UPDATE OR DELETE ON campaigns.center_campaigns
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
CREATE TRIGGER campaign_revision_immutable BEFORE UPDATE OR DELETE ON campaigns.center_campaign_revisions
 FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE campaigns.center_campaigns ENABLE ROW LEVEL SECURITY;
ALTER TABLE campaigns.center_campaigns FORCE ROW LEVEL SECURITY;
ALTER TABLE campaigns.center_campaign_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE campaigns.center_campaign_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY campaign_center_scope ON campaigns.center_campaigns
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
CREATE POLICY campaign_revision_center_scope ON campaigns.center_campaign_revisions
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON campaigns.center_campaigns,campaigns.center_campaign_revisions FROM PUBLIC;
DO $owner$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_owner') THEN
  CREATE ROLE kanoonyar_campaign_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
    NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
  AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe campaign owner'; END IF;
END;$owner$;
GRANT USAGE ON SCHEMA campaigns,programs,centers,governance TO kanoonyar_campaign_owner;
GRANT SELECT ON centers.registry,centers.memberships,programs.center_plan_draft_revisions
 TO kanoonyar_campaign_owner;
GRANT SELECT,INSERT ON campaigns.center_campaigns,campaigns.center_campaign_revisions TO kanoonyar_campaign_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_campaign_owner;
CREATE FUNCTION campaigns.append_center_campaign(
 p_center text,p_campaign uuid,p_expected bigint,p_slug text,p_draft uuid,p_plan_revision bigint,
 p_title text,p_goal text,p_audience text,p_activities jsonb,p_actor text,
 p_audit uuid,p_event uuid,p_correlation text
) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_slug text;v_draft uuid;v_plan_revision bigint;v_current bigint;v_new bigint;
        v_item jsonb;v_key text;v_keys text[]:=ARRAY[]::text[];
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_campaign IS NULL OR p_draft IS NULL
  OR p_expected IS NULL OR p_expected<0 OR p_plan_revision IS NULL OR p_plan_revision<=0
  OR p_slug IS NULL OR length(p_slug)>80 OR p_slug !~ '^[a-z0-9]+(-[a-z0-9]+)*$'
  OR p_title IS NULL OR length(btrim(p_title)) NOT BETWEEN 1 AND 200
  OR p_goal IS NULL OR length(btrim(p_goal)) NOT BETWEEN 1 AND 500
  OR p_audience IS NULL OR length(btrim(p_audience)) NOT BETWEEN 1 AND 240
  OR p_activities IS NULL OR jsonb_typeof(p_activities) IS DISTINCT FROM 'array'
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_event IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid campaign draft' USING ERRCODE='22023'; END IF;
 IF jsonb_array_length(p_activities) NOT BETWEEN 1 AND 12 THEN
  RAISE EXCEPTION 'Invalid campaign activity count' USING ERRCODE='22023'; END IF;
 FOR v_item IN SELECT value FROM jsonb_array_elements(p_activities) LOOP
  IF jsonb_typeof(v_item) IS DISTINCT FROM 'object'
   OR (SELECT count(*) FROM jsonb_object_keys(v_item))<>2
   OR NOT(v_item ? 'activityKey' AND v_item ? 'title')
   OR jsonb_typeof(v_item->'activityKey') IS DISTINCT FROM 'string'
   OR jsonb_typeof(v_item->'title') IS DISTINCT FROM 'string'
   OR (v_item->>'activityKey') !~ '^[a-z][a-z0-9_]{0,31}$'
   OR length(btrim(v_item->>'title')) NOT BETWEEN 1 AND 160
  THEN RAISE EXCEPTION 'Invalid planned activity' USING ERRCODE='22023'; END IF;
  v_key:=v_item->>'activityKey';
  IF v_key=ANY(v_keys) THEN
   RAISE EXCEPTION 'Duplicate activity key' USING ERRCODE='22023'; END IF;
  v_keys:=array_append(v_keys,v_key);
 END LOOP;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
      AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 -- Confirm plan FK independently of the API; missing plan never becomes an implicit plan.
 IF NOT EXISTS(SELECT 1 FROM programs.center_plan_draft_revisions
    WHERE center_id=p_center AND draft_id=p_draft AND revision=p_plan_revision)
 THEN RAISE EXCEPTION 'Referenced center plan revision absent' USING ERRCODE='23503'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('campaign:'||p_center||':'||p_campaign::text,0));
 SELECT slug,draft_id,pinned_plan_revision INTO v_slug,v_draft,v_plan_revision
 FROM campaigns.center_campaigns WHERE center_id=p_center AND campaign_id=p_campaign;
 IF p_expected=0 THEN
  IF v_slug IS NOT NULL THEN RAISE EXCEPTION 'Campaign exists' USING ERRCODE='40001'; END IF;
  INSERT INTO campaigns.center_campaigns(center_id,campaign_id,slug,draft_id,
    pinned_plan_revision,created_by_subject)
  VALUES(p_center,p_campaign,p_slug,p_draft,p_plan_revision,p_actor);
  v_current:=0;
 ELSE
  IF v_slug IS NULL OR v_slug<>p_slug OR v_draft<>p_draft OR v_plan_revision<>p_plan_revision
  THEN RAISE EXCEPTION 'Immutable campaign/plan binding changed' USING ERRCODE='23514'; END IF;
  SELECT COALESCE(max(revision),0) INTO v_current FROM campaigns.center_campaign_revisions
    WHERE center_id=p_center AND campaign_id=p_campaign;
 END IF;
 IF v_current<>p_expected THEN RAISE EXCEPTION 'Stale campaign revision' USING ERRCODE='40001'; END IF;
 v_new:=v_current+1;
 INSERT INTO campaigns.center_campaign_revisions(center_id,campaign_id,revision,
   title,goal_text,audience_description,planned_activities,changed_by_subject)
 VALUES(p_center,p_campaign,v_new,p_title,p_goal,p_audience,p_activities,p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,center_id,
  action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,'CENTER_CAMPAIGN_DRAFT_REVISION_APPENDED',
  'center_campaign_draft',p_campaign::text,p_correlation,
  jsonb_build_object('revision',v_new,'draft_id',p_draft,'pinned_plan_revision',p_plan_revision,
    'activity_count',jsonb_array_length(p_activities),'visibility','PRIVATE_DRAFT'));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,aggregate_id,
  occurred_at,payload)
 VALUES(p_event,p_audit,'CenterCampaignDraftRevisionAppended','center_campaign_draft',
  p_campaign::text,transaction_timestamp(),jsonb_build_object(
   'center_id',p_center,'campaign_id',p_campaign,'revision',v_new,
   'draft_id',p_draft,'pinned_plan_revision',p_plan_revision,
   'visibility','PRIVATE_DRAFT','is_published',false,'is_executed',false));
 RETURN v_new;
END;$fn$;
ALTER FUNCTION campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text)
 OWNER TO kanoonyar_campaign_owner;
REVOKE ALL ON FUNCTION campaigns.append_center_campaign(text,uuid,bigint,text,uuid,bigint,text,text,text,jsonb,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE campaigns.center_campaign_revisions IS
 'Proposed campaign content only; no observed participation, completed activity, money or publication.';
COMMIT;

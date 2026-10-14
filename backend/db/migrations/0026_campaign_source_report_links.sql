-- Sprint 24: immutable associations with existing typed UNVERIFIED source reports.
-- No campaign execution, verified evidence, participant counts or duplicated reports.
BEGIN;
ALTER TABLE campaigns.center_campaigns
 ADD CONSTRAINT campaign_pinned_center_plan_identity
 UNIQUE(center_id,campaign_id,draft_id,pinned_plan_revision);
ALTER TABLE programs.center_activity_reports
 ADD CONSTRAINT report_same_center_plan_identity
 UNIQUE(center_id,report_id,draft_id);
CREATE TABLE campaigns.campaign_report_links(
 center_id text NOT NULL,
 association_id uuid NOT NULL,
 campaign_id uuid NOT NULL,
 campaign_revision bigint NOT NULL CHECK(campaign_revision>0),
 draft_id uuid NOT NULL,
 pinned_plan_revision bigint NOT NULL CHECK(pinned_plan_revision>0),
 activity_key text NOT NULL CHECK(activity_key ~ '^[a-z][a-z0-9_]{0,31}$'),
 report_id uuid NOT NULL,
 linked_by_subject text NOT NULL CHECK(length(btrim(linked_by_subject))>0),
 linked_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,association_id),
 UNIQUE(center_id,campaign_id,report_id),
 FOREIGN KEY(center_id,campaign_id,campaign_revision)
  REFERENCES campaigns.center_campaign_revisions(center_id,campaign_id,revision) ON DELETE RESTRICT,
 FOREIGN KEY(center_id,campaign_id,draft_id,pinned_plan_revision)
  REFERENCES campaigns.center_campaigns(center_id,campaign_id,draft_id,pinned_plan_revision) ON DELETE RESTRICT,
 FOREIGN KEY(center_id,report_id,draft_id)
  REFERENCES programs.center_activity_reports(center_id,report_id,draft_id) ON DELETE RESTRICT
);
CREATE INDEX campaign_report_links_order ON campaigns.campaign_report_links(center_id,campaign_id,association_id DESC);
CREATE TRIGGER campaign_report_association_immutable BEFORE UPDATE OR DELETE
 ON campaigns.campaign_report_links FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON campaigns.campaign_report_links FROM PUBLIC;
ALTER TABLE campaigns.campaign_report_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE campaigns.campaign_report_links FORCE ROW LEVEL SECURITY;
CREATE POLICY campaign_report_center_scope ON campaigns.campaign_report_links
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));

DO $owner$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_report_link_owner') THEN
  CREATE ROLE kanoonyar_campaign_report_link_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
   NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_report_link_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe association function owner'; END IF;
END;$owner$;
GRANT USAGE ON SCHEMA campaigns,programs,centers,governance
 TO kanoonyar_campaign_report_link_owner;
GRANT SELECT ON centers.registry,centers.memberships,
 campaigns.center_campaigns,campaigns.center_campaign_revisions,
 programs.center_activity_reports,programs.activity_report_source_bindings,
 programs.center_plan_issuance_links TO kanoonyar_campaign_report_link_owner;
GRANT SELECT,INSERT ON campaigns.campaign_report_links TO kanoonyar_campaign_report_link_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_campaign_report_link_owner;

CREATE FUNCTION campaigns.link_unverified_source_report(
 p_center text,p_association uuid,p_campaign uuid,p_campaign_revision bigint,
 p_activity_key text,p_report uuid,p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER
 SET search_path=pg_catalog,pg_temp AS $fn$
DECLARE v_draft uuid;v_pinned bigint;
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_association IS NULL OR p_campaign IS NULL
  OR p_campaign_revision IS NULL OR p_campaign_revision<1
  OR p_activity_key IS NULL OR p_activity_key !~ '^[a-z][a-z0-9_]{0,31}$'
  OR p_report IS NULL OR p_actor IS NULL OR btrim(p_actor)=''
  OR p_audit IS NULL OR p_event IS NULL OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid campaign source association' USING ERRCODE='22023'; END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
    ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
     AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 SELECT draft_id,pinned_plan_revision INTO v_draft,v_pinned
  FROM campaigns.center_campaigns WHERE center_id=p_center AND campaign_id=p_campaign;
 IF v_draft IS NULL OR NOT EXISTS(
  SELECT 1 FROM campaigns.center_campaign_revisions v
  WHERE v.center_id=p_center AND v.campaign_id=p_campaign
   AND v.revision=p_campaign_revision AND EXISTS(
    SELECT 1 FROM jsonb_array_elements(v.planned_activities) a
    WHERE a->>'activityKey'=p_activity_key))
 THEN RAISE EXCEPTION 'Planned activity/revision missing' USING ERRCODE='23503'; END IF;
 -- Source report must be typed, source-owned and bound to an issuance linking
 -- the VERY SAME pinned local-plan revision (not merely its draft identity).
 IF NOT EXISTS(
  SELECT 1 FROM programs.center_activity_reports r
  JOIN programs.activity_report_source_bindings b
    ON b.center_id=r.center_id AND b.report_id=r.report_id
   AND b.source_kind=r.source_kind AND b.source_record_ref=r.source_record_ref
  JOIN programs.center_plan_issuance_links l
    ON l.center_id=r.center_id AND l.link_id=r.link_id AND l.draft_id=r.draft_id
  WHERE r.center_id=p_center AND r.report_id=p_report
   AND r.draft_id=v_draft AND l.pinned_revision=v_pinned
   AND r.source_kind IN('CLASS_SESSION','CENTER_EVENT')
 ) THEN RAISE EXCEPTION 'Exact typed source report and pinned plan missing' USING ERRCODE='23503'; END IF;
 INSERT INTO campaigns.campaign_report_links(
  center_id,association_id,campaign_id,campaign_revision,draft_id,pinned_plan_revision,
  activity_key,report_id,linked_by_subject)
 VALUES(p_center,p_association,p_campaign,p_campaign_revision,v_draft,v_pinned,
  p_activity_key,p_report,p_actor);
 INSERT INTO governance.command_audit(
  audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,target_id,
  correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
 'UNVERIFIED_CAMPAIGN_REPORT_ASSOCIATION_RECORDED','campaign_report_link',
 p_association::text,p_correlation,
 jsonb_build_object('campaign_id',p_campaign,'campaign_revision',p_campaign_revision,
  'report_id',p_report,'verification_status','UNVERIFIED'));
 INSERT INTO governance.outbox_events(
  event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'UnverifiedCampaignReportLinked','campaign_report_link',
 p_association::text,transaction_timestamp(),
 jsonb_build_object('center_id',p_center,'association_id',p_association,
  'campaign_id',p_campaign,'campaign_revision',p_campaign_revision,
  'report_id',p_report,'verification_status','UNVERIFIED','is_executed',false));
 RETURN p_association;
END;$fn$;
ALTER FUNCTION campaigns.link_unverified_source_report(
 text,uuid,uuid,bigint,text,uuid,text,uuid,uuid,text)
 OWNER TO kanoonyar_campaign_report_link_owner;
REVOKE ALL ON FUNCTION campaigns.link_unverified_source_report(
 text,uuid,uuid,bigint,text,uuid,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE campaigns.campaign_report_links IS
 'Association only: original typed report is UNVERIFIED; no performance or participation counts.';
COMMIT;

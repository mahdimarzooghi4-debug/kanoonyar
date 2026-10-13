-- Sprint 25: reviewer inspection notes are observations, NEVER verification/approval.
BEGIN;
CREATE TABLE campaigns.campaign_report_inspection_notes (
 center_id text NOT NULL,
 note_id uuid NOT NULL,
 campaign_id uuid NOT NULL,
 association_id uuid NOT NULL,
 note_text text NOT NULL CHECK(length(btrim(note_text)) BETWEEN 1 AND 1000),
 noted_by_subject text NOT NULL CHECK(length(btrim(noted_by_subject))>0),
 noted_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 PRIMARY KEY(center_id,note_id),
 FOREIGN KEY(center_id,association_id) REFERENCES campaigns.campaign_report_links(center_id,association_id) ON DELETE RESTRICT,
 FOREIGN KEY(center_id,campaign_id) REFERENCES campaigns.center_campaigns(center_id,campaign_id) ON DELETE RESTRICT
);
CREATE INDEX campaign_report_inspection_history ON campaigns.campaign_report_inspection_notes
 (center_id,campaign_id,association_id,note_id DESC);
CREATE TRIGGER campaign_inspection_note_immutable BEFORE UPDATE OR DELETE
 ON campaigns.campaign_report_inspection_notes FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
REVOKE ALL ON campaigns.campaign_report_inspection_notes FROM PUBLIC;
ALTER TABLE campaigns.campaign_report_inspection_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE campaigns.campaign_report_inspection_notes FORCE ROW LEVEL SECURITY;
CREATE POLICY campaign_inspection_center_scope ON campaigns.campaign_report_inspection_notes
 USING(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_inspection_owner') THEN
 CREATE ROLE kanoonyar_campaign_inspection_owner NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
 NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_campaign_inspection_owner'
 AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
 AND NOT rolcreaterole AND NOT rolreplication) THEN
 RAISE EXCEPTION 'Unsafe campaign note owner'; END IF;
END;$role$;
GRANT USAGE ON SCHEMA campaigns,centers,governance TO kanoonyar_campaign_inspection_owner;
GRANT SELECT ON centers.registry,centers.memberships,campaigns.campaign_report_links
 TO kanoonyar_campaign_inspection_owner;
GRANT SELECT,INSERT ON campaigns.campaign_report_inspection_notes TO kanoonyar_campaign_inspection_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_campaign_inspection_owner;
CREATE FUNCTION campaigns.record_campaign_report_inspection(
 p_center text,p_campaign uuid,p_association uuid,p_note uuid,
 p_text text,p_actor text,p_audit uuid,p_event uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_center IS NULL OR btrim(p_center)='' OR p_campaign IS NULL
 OR p_association IS NULL OR p_note IS NULL OR p_text IS NULL
 OR length(btrim(p_text)) NOT BETWEEN 1 AND 1000
 OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL
 OR p_event IS NULL OR p_correlation IS NULL OR btrim(p_correlation)=''
 THEN RAISE EXCEPTION 'Invalid source inspection note' USING ERRCODE='22023'; END IF;
 IF p_center IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
 OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
 ON c.center_id=m.center_id WHERE m.center_id=p_center AND m.subject_id=p_actor
 AND m.is_active AND c.is_active)
 THEN RAISE EXCEPTION 'Center scope or active membership absent' USING ERRCODE='42501'; END IF;
 IF NOT EXISTS(SELECT 1 FROM campaigns.campaign_report_links l
 WHERE l.center_id=p_center AND l.campaign_id=p_campaign AND l.association_id=p_association)
 THEN RAISE EXCEPTION 'Campaign association absent' USING ERRCODE='23503'; END IF;
 INSERT INTO campaigns.campaign_report_inspection_notes(
 center_id,note_id,campaign_id,association_id,note_text,noted_by_subject)
 VALUES(p_center,p_note,p_campaign,p_association,btrim(p_text),p_actor);
 INSERT INTO governance.command_audit(
 audit_id,occurred_at,actor_subject_id,center_id,action_code,target_type,target_id,
 correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_center,
 'CAMPAIGN_REPORT_INSPECTION_NOTE_RECORDED','campaign_report_inspection_note',
 p_note::text,p_correlation,
 jsonb_build_object('campaign_id',p_campaign,'association_id',p_association,
 'verification_status','UNVERIFIED','is_approval',false));
 INSERT INTO governance.outbox_events(
 event_id,audit_id,event_type,aggregate_type,aggregate_id,occurred_at,payload)
 VALUES(p_event,p_audit,'CampaignReportInspectionNoteRecorded',
 'campaign_report_inspection_note',p_note::text,transaction_timestamp(),
 jsonb_build_object('center_id',p_center,'campaign_id',p_campaign,
 'association_id',p_association,'note_id',p_note,
 'verification_status','UNVERIFIED','is_approval',false,'is_executed',false));
 RETURN p_note;
END;$fn$;
ALTER FUNCTION campaigns.record_campaign_report_inspection(
 text,uuid,uuid,uuid,text,text,uuid,uuid,text) OWNER TO kanoonyar_campaign_inspection_owner;
REVOKE ALL ON FUNCTION campaigns.record_campaign_report_inspection(
 text,uuid,uuid,uuid,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE campaigns.campaign_report_inspection_notes IS
 'Human notes about UNVERIFIED source reports only. No reviewer qualification, approval, verified evidence or execution decision.';
COMMIT;
-- Sprint 88: align attendance history pages with the keyset sort order.
BEGIN;
CREATE INDEX session_attendance_page_idx
 ON learning.session_attendance_claims
   (center_id,session_id,occurrence_report_id,recorded_at DESC,claim_id DESC);
COMMENT ON INDEX learning.session_attendance_page_idx IS
 'Supports center- and occurrence-scoped UNVERIFIED attendance history keyset pages; contains no verification or authority state.';
COMMIT;

--
-- The author disclaims copyright to this source code.  In place of
-- a legal notice, here is a blessing:
--
--    May you do good and not evil.
--    May you find forgiveness for yourself and forgive others.
--    May you share freely, never taking more than you give.
--
-----------------------------------------------------------------------------------------
-- This file contains code used to implement the performance scripts
--
-- Repository path:    $HeadURL: https://sqlitecs.googlecode.com/svn/trunk/test/performance.sql $
-- Last Revised      : $Revision: 62 $ 
-- Last Changed By   : $LastChangedBy: noah.hart $ 
-- Last Changed Date : $LastChangedDate: 2009-08-03 09:19:48 -0700 (Mon, 03 Aug 2009) $ 
-----------------------------------------------------------------------------------------
---------------------------------------------------------------------
--
-- NOTES: 
--
---------------------------------------------------------------------

------------------------------------------
-- LEVEL THE PLAYING FIELD WITH PRAGMAs
------------------------------------------

PRAGMA auto_vacuum = NONE;
PRAGMA cache_size = 20000;
PRAGMA count_changes = 1;
PRAGMA encoding = "UTF-8";
PRAGMA fullfsync = 0;
PRAGMA journal_mode = NONE;
PRAGMA locking_mode = EXCLUSIVE;
PRAGMA page_size = 1024;
PRAGMA synchronous = OFF; 
PRAGMA temp_store = MEMORY;
------------------------------------------
-- A LITTLE SETUP BEFORE WE BEGIN
------------------------------------------

ATTACH ':memory:' as tDB;
CREATE TABLE tDB.TIMER(TestNumber INTEGER, Description TEXT, StartTime REAL, EndTime REAL DEFAULT NULL, Rows INTEGER DEFAULT NULL);
INSERT INTO TIMER VALUES(0, 'performance.txt,v 1.3', 0, 1, 0);
CREATE TABLE tDB.TEST1 (I INTEGER, T TEXT);
CREATE TABLE N_1(i INTEGER, t TEXT);
INSERT INTO N_1 VALUES(1, 't1_');
INSERT INTO N_1 VALUES(2, 't_22_');
INSERT INTO N_1 VALUES(3, 'tx_3_3_3_');
INSERT INTO N_1 VALUES(4, 'txt_4_4_4_4_');
CREATE TABLE N_2(i INTEGER, t TEXT);
INSERT INTO N_2 SELECT N1.I+N2.I*7, N1.T||N2.T FROM N_1 N1 CROSS JOIN N_1 N2 CROSS JOIN N_1 N3;

--------------------------------------------------------
-- TEST 1 
-- TRIVIAL INSERTS -- KEEP THE NUMBER AND TEXT SMALL
--------------------------------------------------------
BEGIN;   
INSERT INTO TIMER (TestNumber, Description, StartTime) SELECT 1+MAX(TESTNUMBER), 'Trivial Inserts', (julianday('now') - 2440587.5)*86400 FROM TIMER;
INSERT INTO TEST1 SELECT 1,'T' FROM N_2 N1 CROSS JOIN N_2 N2 CROSS JOIN N_2 N3;
COMMIT;

 	UPDATE TIMER SET EndTime = (julianday('now') - 2440587.5)*86400.0, Rows = changes() 
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);

--------------------------------------------------------
-- TEST 2
-- TRIVIAL SELECTS 
--------------------------------------------------------
INSERT INTO TIMER (TestNumber, Description, StartTime) SELECT 1+MAX(TESTNUMBER), 'Trivial Selects', (julianday('now') - 2440587.5)*86400 FROM TIMER;
 	UPDATE TIMER SET Rows = (SELECT COUNT(*) FROM TEST1 where rowid > 0)
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);
 	UPDATE TIMER SET EndTime = (julianday('now') - 2440587.5)*86400.0
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);

--------------------------------------------------------
-- TEST 3
-- TRIVIAL UPDATES -- THE NUMBERS AND ROW SIZE ARE SMALL
--------------------------------------------------------
BEGIN;   
INSERT INTO TIMER (TestNumber, Description, StartTime) SELECT 1+MAX(TESTNUMBER), 'Trivial Updates', (julianday('now') - 2440587.5)*86400 FROM TIMER;
UPDATE TEST1 SET I=I;
COMMIT;
 	UPDATE TIMER SET EndTime = (julianday('now') - 2440587.5)*86400.0, Rows = changes()
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);

--------------------------------------------------------
-- TEST 4
-- TRIVIAL DELETES
--------------------------------------------------------
BEGIN;   
INSERT INTO TIMER (TestNumber, Description, StartTime) SELECT 1+MAX(TESTNUMBER), 'Trivial Deletes', (julianday('now') - 2440587.5)*86400 FROM TIMER;
DELETE FROM TEST1 WHERE I >0;
COMMIT;
 	UPDATE TIMER SET EndTime = (julianday('now') - 2440587.5)*86400.0, Rows = changes()
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);

------------------------------------------
-- A LITTLE CLEANUP BEFORE WE CONTINUE
------------------------------------------

DROP TABLE TEST1;
CREATE TABLE tDB.TEST1 (I INTEGER, T TEXT);
PRAGMA page_count;
VACUUM;
PRAGMA page_count;

--------------------------------------------------------
-- TEST 5
-- INSERTS WITH CALCULATIONS -- SHOULD BE SLOWER THAN 1
--------------------------------------------------------
BEGIN;   
INSERT INTO TIMER (TestNumber, Description, StartTime) SELECT 1+MAX(TESTNUMBER), 'Insert with calculations', (julianday('now') - 2440587.5)*86400 FROM TIMER;
INSERT INTO TEST1 SELECT N1.I*N2.I+N3.I, N1.T||N2.T||N3.T FROM N_2 N1 CROSS JOIN N_2 N2 CROSS JOIN N_2 N3;
COMMIT;
 	UPDATE TIMER SET EndTime = (julianday('now') - 2440587.5)*86400.0, Rows =  changes()
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);

--------------------------------------------------------
-- TEST 6
-- UPDATES WITH CALCULATIONS -- SHOULD BE SLOWER THAN 2
--------------------------------------------------------
BEGIN;   
INSERT INTO TIMER (TestNumber, Description, StartTime) SELECT 1+MAX(TESTNUMBER), 'Updates with calculations and longer rows', (julianday('now') - 2440587.5)*86400 FROM TIMER;
UPDATE TEST1 SET I=I*1+2-3;
COMMIT;
 	UPDATE TIMER SET EndTime = (julianday('now') - 2440587.5)*86400.0, Rows = changes()
 		WHERE TestNumber = (SELECT MAX(TESTNUMBER) FROM TIMER);

-----------------------------------------------
-- REPORT THE RESULTS
--------------------------------------------------------
 	Select TestNumber, Description, ROUND(EndTime- StartTime,2), Rows, Round(Rows/(EndTime-StartTime)/1000)||'K Rows/Second' from TIMER;



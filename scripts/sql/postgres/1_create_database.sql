CREATE DATABASE cryptodd
    WITH 
    OWNER = cryptodduser
    TEMPLATE = template0
    ENCODING = 'UTF8'
    LC_COLLATE = 'C'
    LC_CTYPE = 'C'
    CONNECTION LIMIT = -1;

GRANT ALL ON DATABASE cryptodd TO cryptodduser;
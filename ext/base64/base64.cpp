
//*********************************************************************
//* Base64 - a simple base64 encoder and decoder.
//*
//*     Copyright (c) 1999, Bob Withers - bwit@pobox.com
//*
//* This code may be freely used for any purpose, either personal
//* or commercial, provided the authors copyright notice remains
//* intact.
//*********************************************************************

#include "base64.h"

using std::string;

const char          fillchar = '=';

                        // 00000000001111111111222222
                        // 01234567890123456789012345a
static string       cvt = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"

                        // 22223333333333444444444455
                        // 67890123456789012345678901
                          "abcdefghijklmnopqrstuvwxyz"

                        // 555555556666
                        // 234567890123
                          "0123456789+/";

string base64::encode(const string& data)
{
    auto     string::size_type  i;
    auto     char               c;
    auto     string::size_type  len = data.length();
    auto     string             ret;

    for (i = 0; i < len; ++i)
    {
        c = (data[i] >> 2) & 0x3f;
        ret.append(1, cvt[c]);
        c = (data[i] << 4) & 0x3f;
        if (++i < len)
            c |= (data[i] >> 4) & 0x0f;

        ret.append(1, cvt[c]);
        if (i < len)
        {
            c = (data[i] << 2) & 0x3f;
            if (++i < len)
                c |= (data[i] >> 6) & 0x03;

            ret.append(1, cvt[c]);
        }
        else
        {
            ++i;
            ret.append(1, fillchar);
        }

        if (i < len)
        {
            c = data[i] & 0x3f;
            ret.append(1, cvt[c]);
        }
        else
        {
            ret.append(1, fillchar);
        }
    }

    return(ret);
}

string base64::decode(const string& data)
{
    auto     string::size_type  i;
    auto     char               c;
    auto     char               c1;
    auto     string::size_type  len = data.length();
    auto     string             ret;

    for (i = 0; i < len; ++i)
    {
        c = (char) cvt.find(data[i]);
        ++i;
        c1 = (char) cvt.find(data[i]);
        c = (c << 2) | ((c1 >> 4) & 0x3);
        ret.append(1, c);
        if (++i < len)
        {
            c = data[i];
            if (fillchar == c)
                break;

            c = (char) cvt.find(c);
            c1 = ((c1 << 4) & 0xf0) | ((c >> 2) & 0xf);
            ret.append(1, c1);
        }

        if (++i < len)
        {
            c1 = data[i];
            if (fillchar == c1)
                break;

            c1 = (char) cvt.find(c1);
            c = ((c << 6) & 0xc0) | c1;
            ret.append(1, c);
        }
    }

    return(ret);
}

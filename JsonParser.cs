/*
 * Copyright (c) 2013 Cyril Jandia
 *
 * http://www.cjandia.com/
 *
Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
``Software''), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ``AS IS'', WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL CYRIL JANDIA BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

Except as contained in this notice, the name of Cyril Jandia shall
not be used in advertising or otherwise to promote the sale, use or
other dealings in this Software without prior written authorization
from Cyril Jandia.

Inquiries : ysharp {dot} design {at} gmail {dot} com
 *
 */

// On GitHub:
// https://github.com/ysharplanguage/JSONParser

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace System.Text.Json
{
    public interface ITypeCache
    {
        int Entry(Type type);
    }

    public class JsonParser : ITypeCache
    {
        internal class Compiled
        {
            internal static Func<object> Ctor(Type clr)
            {
                return Ctor(clr, null);
            }

            internal static Func<object> Ctor(Type clr, Type elt)
            {
                var type = ((elt != null) ? clr.MakeGenericType(elt) : clr);
                var ctor =
                    type.GetConstructor
                    (
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.CreateInstance,
                        null, System.Type.EmptyTypes, null
                    );
                if (ctor != null)
                {
                    var dyn = new System.Reflection.Emit.DynamicMethod("", typeof(object), null, type, true);
                    var il = dyn.GetILGenerator();
                    il.Emit(System.Reflection.Emit.OpCodes.Newobj, ctor);
                    il.Emit(System.Reflection.Emit.OpCodes.Ret);
                    return (Func<object>)dyn.CreateDelegate(typeof(Func<object>));
                }
                else
                    return null;
            }

            internal static Action<object, object> PropSet(Type clr, System.Reflection.PropertyInfo pi)
            {
                var dyn = new System.Reflection.Emit.DynamicMethod("", null, new Type[] { typeof(object), typeof(object) }, clr);
                var il = dyn.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                if (pi.PropertyType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, pi.PropertyType);
                il.Emit(System.Reflection.Emit.OpCodes.Callvirt, pi.GetSetMethod());
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return (Action<object, object>)dyn.CreateDelegate(typeof(Action<object, object>));
            }

            internal static IDictionary<string, PropInfo> GetPropInfos(ITypeCache cache, Type clr)
            {
                var props = new Dictionary<string, PropInfo>();
                var pis = clr.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                foreach (var pi in pis)
                    if (pi.CanWrite)
                        props.Add(pi.Name, new PropInfo(cache, pi.PropertyType, pi.Name, PropSet(clr, pi)));
                return props;
            }

            internal static Type Realizes(Type type, Type generic)
            {
                var itfs = type.GetInterfaces();
                foreach (var it in itfs)
                    if (it.IsGenericType && it.GetGenericTypeDefinition() == generic)
                        return type;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == generic)
                    return type;
                if (type.BaseType == null)
                    return null;
                return Realizes(type.BaseType, generic);
            }
        }

        internal class PropInfo
        {
            internal readonly Type Clr;
            internal readonly int Type;
            internal readonly string Name;
            internal readonly Action<object, object> PropSet;

            internal PropInfo(ITypeCache cache, Type clr, string name, Action<object, object> propSet)
            {
                Type = cache.Entry(Clr = clr);
                Name = name;
                PropSet = propSet;
            }
        }

        internal class PropInfos
        {
            internal readonly PropInfo[][] First = new PropInfo[128][];

            internal PropInfos(IDictionary<string, PropInfo> propInfos)
            {
                for (var ch = 'A'; ch <= 'z'; ch++)
                {
                    if (IDF[ch])
                    {
                        var sp = new SortedDictionary<string, PropInfo>();
                        foreach (var pi in propInfos)
                        {
                            if (pi.Key[0] == ch)
                                sp.Add(pi.Key, pi.Value);
                        }
                        if (sp.Values.Count > 0)
                            First[ch] = sp.Values.ToArray();
                    }
                }
            }
        }

        internal class TypeInfo
        {
            internal readonly Type Clr;
            internal readonly int ElementType;
            internal readonly Func<object> ObjCtor;
            internal readonly Func<object> ArrCtor;

            internal TypeInfo(ITypeCache cache, Type clr)
            {
                Clr = clr;
                if (clr != TYPEOF[VOID])
                {
                    Type[] ga;
                    Type ie, elt = (((ie = Compiled.Realizes(clr, typeof(IEnumerable<>))) != null) ? ((((ga = ie.GetGenericArguments()) != null) && (ga.Length > 0)) ? ga[0] : null) : null);
                    ElementType = ((elt != null) ? cache.Entry(elt) : VOID);
                    ObjCtor = Compiled.Ctor(clr);
                    ArrCtor = Compiled.Ctor(typeof(List<>), clr);
                    ((JsonParser)cache).SetPropInfos(Compiled.GetPropInfos(cache, clr));
                }
            }
        }

        private static readonly Type[] TYPEOF = new Type[4];
        private const int VOID = 0;
        private const int OBJECT = 1;
        private const int DOUBLE = 2;
        private const int STRING = 3;
        private static readonly char[] ESC = new char[128];
        private static readonly bool[] IDF = new bool[128];
        private static readonly bool[] IDN = new bool[128];
        private const char ANY = char.MinValue;
        private const int EOF = -1;
        private const int LBS = 128;

        private IDictionary<Type, int> rtti = new Dictionary<Type, int>();
        private PropInfos[] props = new PropInfos[128];
        private TypeInfo[] types = new TypeInfo[128];

        private Func<int, object>[] parse = new Func<int, object>[128];
        private char[] lbf = new char[LBS];
        private char[] stc = new char[1];
        private StringBuilder lsb;
        private Func<int> Read;
        private StreamReader str;
        private string txt;
        private int len;
        private int lln;
        private int chr;
        private int at;

        internal void SetPropInfos(IDictionary<string, PropInfo> propInfos)
        {
            props[rtti.Count] = new PropInfos(propInfos);
        }

        static JsonParser()
        {
            TYPEOF[VOID] = typeof(void);
            TYPEOF[OBJECT] = typeof(object);
            TYPEOF[DOUBLE] = typeof(double);
            TYPEOF[STRING] = typeof(string);
            ESC['/'] = '/'; ESC['\\'] = '\\';
            ESC['b'] = '\b'; ESC['f'] = '\f'; ESC['n'] = '\n'; ESC['r'] = '\r'; ESC['t'] = '\t';
            for (int c = 'A'; c <= 'Z'; c++) IDF[c] = IDN[c] = IDF[c + 32] = IDN[c + 32] = true;
            IDF['_'] = IDN['_'] = true;
            for (int c = '0'; c <= '9'; c++) IDN[c] = true;
        }

        private Exception Error(string message) { return new Exception(String.Format("{0} at {1} (found: '{2}'{3})", message, at, ((chr > EOF) ? ("" + (char)chr) : "EOF"), chr)); }
        private int FromStream() { return (chr = ((str.Read(stc, 0, 1) > 0) ? stc[0] : EOF)); }
        private int FromString() { return (chr = ((++at < len) ? txt[at] : EOF)); }
        private void Reset(Func<int> read) { chr = ANY; at = EOF; Read = read; }
        private int Next(char ch) { if (chr != ch) throw Error("Unexpected character"); return Read(); }
        private void Space() { if ((chr > EOF) && (chr <= ' ')) while ((Read() <= ' ') && (chr > EOF)); }

        private object Error(int outer) { throw Error("Bad value"); }
        private object Null(int outer) { Next('n'); Next('u'); Next('l'); Next('l'); return null; }
        private object False(int outer) { Next('f'); Next('a'); Next('l'); Next('s'); Next('e'); return false; }
        private object True(int outer) { Next('t'); Next('r'); Next('u'); Next('e'); return true; }

        private string Chars()
        {
            var chars = ((lsb != null) ? lsb.ToString() : new string(lbf, 0, lln));
            return chars;
        }

        private void Append(int c)
        {
            if (c > EOF)
            {
                if (lln < LBS)
                    lbf[lln++] = (char)c;
                else
                {
                    lsb = (lsb ?? new StringBuilder().Append(new string(lbf, 0, lln)));
                    lsb.Append((char)c);
                }
            }
            else
            {
                lsb = null;
                lln = 0;
            }
        }

        private object Num(int outer)
        {
            var type = (outer >> 1);
            Append(EOF);
            if (chr == '-')
            {
                Append(chr);
                Read();
            }
            while ((chr >= '0') && (chr <= '9'))
            {
                Append(chr);
                Read();
            }
            if (chr == '.')
            {
                Append(chr);
                while ((Read() > EOF) && (chr >= '0') && (chr <= '9'))
                    Append(chr);
            }
            if ((chr == 'e') || (chr == 'E'))
            {
                Append(chr);
                Read();
                if ((chr == '-') || (chr == '+'))
                {
                    Append(chr);
                    Read();
                }
                while ((chr >= '0') && (chr <= '9'))
                {
                    Append(chr);
                    Read();
                }
            }
            return (((type > OBJECT) && (type != STRING)) ? Convert.ChangeType(Chars(), types[type].Clr) : Chars());
        }

        private object Literal(int outer)
        {
            var lkup = (outer < VOID);
            var type = ((lkup ? -outer : outer) >> 1);
            var p = -1;
            if (chr == '"')
            {
                int c = chr, i = 1, n = 0;
                PropInfo[] pi = null;
                Read();
                Append(EOF);
                if ((type > OBJECT) && lkup)
                {
                    var tpi = props[type].First;
                    lkup = ((chr > EOF) && (chr < 128) && IDF[chr] && (tpi[chr] != null));
                    if (lkup)
                    {
                        pi = tpi[chr];
                        n = pi.Length;
                        p = 0;
                    }
                }
                while ((chr > EOF) && ((chr != '"') || (c == '\\')))
                {
                    var r = (char)chr;
                    if (c == '\\')
                    {
                        if ((r < 128) && (ESC[r] > ANY))
                            Append(ESC[r]);
                        else
                            Append(r);
                    }
                    else
                    {
                        if (r != '\\')
                            Append(r);
                    }
                    c = chr;
                    Read();
                    if ((p >= 0) && (chr > EOF) && (chr < 128) && IDN[chr])
                    {
                        string pn;
                        while ((p < n) && (i < (pn = pi[p].Name).Length) && (chr > pn[i]))
                            p++;
                        if ((p >= n) || (i >= (pn = pi[p].Name).Length) || (chr < pn[i]))
                            p = -1;
                    }
                    i++;
                }
                if (chr == '"')
                {
                    Read();
                    return ((p >= 0) ? (object)pi[p] : Chars());
                }
            }
            throw Error((type > OBJECT) ? "Bad literal" : "Bad key");
        }

        private object Str(int outer)
        {
            var type = (outer >> 1);
            return (((type > OBJECT) && (type != STRING)) ? Convert.ChangeType(Literal(outer), types[type].Clr) : Literal(outer));
        }

        private object Obj(int outer)
        {
            var type = (outer >> 1);
            if (chr == '{')
            {
                IDictionary<string, object> d = null;
                TypeInfo ti = null;
                object o = null;
                Read();
                Space();
                if (type > OBJECT)
                    o = (ti = types[type]).ObjCtor();
                else
                    d = new Dictionary<string, object>();
                if (chr == '}')
                {
                    Read();
                    return (o ?? d);
                }
                while (chr > EOF)
                {
                    object k = Literal(-outer);
                    var pi = (k as PropInfo);
                    Space();
                    Next(':');
                    if (pi != null)
                    {
                        //FIXME: quick hack to be able to pass the burning monk's deserialization test, at:
                        //https://github.com/theburningmonk/SimpleSpeedTester
                        if (lbf[0] != '$')
                        {
                            var v = Val(pi.Type);
                            pi.PropSet(o, v);
                        }
                        else
                            Val(OBJECT << 1);
                    }
                    else
                    {
                        var v = Val(OBJECT << 1);
                        d[(string)k] = v;
                    }
                    Space();
                    if (chr == '}')
                    {
                        Read();
                        return (o ?? d);
                    }
                    Next(',');
                    Space();
                }
            }
            throw Error("Bad object");
        }

        private object Arr(int outer)
        {
            var array = ((outer % 2) != 0);
            var type = (outer >> 1);
            var inner = types[type].ElementType;
            if (chr == '[')
            {
                TypeInfo ti;
                IList l;
                Read();
                Space();
                l = (IList)(ti = types[(inner > VOID) ? (inner >> 1) : type]).ArrCtor();
                if (chr == ']')
                {
                    Read();
                    if (array)
                    {
                        var a = Array.CreateInstance(ti.Clr, l.Count);
                        l.CopyTo(a, 0);
                        return a;
                    }
                    else
                        return l;
                }
                while (chr > EOF)
                {
                    var v = Val(!array ? ((inner > VOID) ? inner : outer) : outer);
                    l.Add(v);
                    Space();
                    if (chr == ']')
                    {
                        Read();
                        if (array)
                        {
                            var a = Array.CreateInstance(ti.Clr, l.Count);
                            l.CopyTo(a, 0);
                            return a;
                        }
                        else
                            return l;
                    }
                    Next(',');
                    Space();
                }
            }
            throw Error("Bad array");
        }

        private object Val(int outer)
        {
            Space();
            return parse[(chr + 1) & 0x7f](outer);
        }

        private T DoParse<T>(Stream input)
        {
            Reset(FromStream);
            using (str = new StreamReader(input))
            {
                return (T)Val(Entry(typeof(T)));
            }
        }

        private T DoParse<T>(string input)
        {
            Reset(FromString);
            txt = (string)input;
            len = txt.Length;
            return (T)Val(Entry(typeof(T)));
        }

        public JsonParser()
        {
            parse['n' + 1] = Null;
            parse['f' + 1] = False;
            parse['t' + 1] = True;
            parse['0' + 1] = parse['1' + 1] = parse['2' + 1] = parse['3' + 1] = parse['4' + 1] =
            parse['5' + 1] = parse['6' + 1] = parse['7' + 1] = parse['8' + 1] = parse['9' + 1] =
            parse['-' + 1] = Num;
            parse['"' + 1] = Str;
            parse['{' + 1] = Obj;
            parse['[' + 1] = Arr;
            for (var input = 0; input < 128; input++)
                parse[input] = (parse[input] ?? Error);
            for (var type = VOID; type < TYPEOF.Length; type++)
                Entry(TYPEOF[type]);
        }

        public int Entry(Type type)
        {
            bool array = type.IsArray;
            int cached;
            type = (array ? type.GetElementType() : type);
            if (!rtti.TryGetValue(type, out cached))
            {
                var ti = new TypeInfo(this, type);
                types[cached = rtti.Count] = ti;
                rtti.Add(type, cached);
            }
            return ((cached << 1) + (array ? 1 : 0));
        }

        public T Parse<T>(Stream input)
        {
            return DoParse<T>(input);
        }

        public T Parse<T>(string input)
        {
            return DoParse<T>(input);
        }
    }
}

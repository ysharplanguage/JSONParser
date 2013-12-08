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
using System.Linq;
using System.Text;

namespace System.Text.Json
{
    public class JsonParser
    {
        private static readonly char[] ESC = new char[128];
        private static readonly bool[] IDF = new bool[128];
        private static readonly bool[] IDN = new bool[128];
        private const char ANY = char.MinValue;
        private const char EOF = char.MaxValue;
        private const int LBS = 128;
        private const int TDS = 128;
        private const int OBJECT = 0;
        private const int ARRAY = 1;
        private const int STRING = 2;
        private const int DOUBLE = 3;

        private IDictionary<Type, int> rtti = new Dictionary<Type, int>();
        private TypeInfo[] types = new TypeInfo[TDS];

        private Func<int, object>[] parse = new Func<int, object>[128];
        private StringBuilder lsb = new StringBuilder();
        private char[] stc = new char[2] { EOF, ANY };
        private char[] lbf = new char[LBS];
        private Func<char> Read;
        private System.IO.StreamReader str;
        private char[] txt;
        private char chr;
        private int len;
        private int at;

        internal class PropInfo
        {
            internal Action<object, object> Set;
            internal Type Type;
        }

        internal class TypeInfo
        {
            internal IDictionary<string, PropInfo> Props;
            internal Func<object> Ctor;
            internal Type ElementType;
            internal Type Type;
            internal int Inner;

            internal TypeInfo(Type type, Func<object> ctor)
            {
                var pis =
                    type.
                    GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).
                    Where(p => p.CanWrite).
                    ToArray();
                Type = type;
                Ctor = ctor;
                Props = new Dictionary<string, PropInfo>();
                for (var i = 0; i < pis.Length; i++)
                    Props.Add(pis[i].Name, PropInfo(pis[i]));
            }

            private static PropInfo PropInfo(System.Reflection.PropertyInfo pi)
            {
                var dyn = new System.Reflection.Emit.DynamicMethod("", null, new Type[] { typeof(object), typeof(object) }, typeof(PropInfo));
                var il = dyn.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                if (pi.PropertyType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, pi.PropertyType);
                il.Emit(System.Reflection.Emit.OpCodes.Callvirt, pi.GetSetMethod());
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                return new PropInfo { Type = pi.PropertyType, Set = (Action<object, object>)dyn.CreateDelegate(typeof(Action<object, object>)) };
            }
        }

        private static Func<object> Ctor(Type clr, bool list)
        {
            var type = (list ? typeof(List<>).MakeGenericType(clr) : clr);
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

        static JsonParser()
        {
            ESC['/'] = '/'; ESC['\\'] = '\\';
            ESC['b'] = '\b'; ESC['f'] = '\f'; ESC['n'] = '\n'; ESC['r'] = '\r'; ESC['t'] = '\t';
            for (int c = ANY; c < 128; c++) if (ESC[c] == ANY) ESC[c] = (char)c;
            for (int c = 'A'; c <= 'Z'; c++) IDF[c] = IDN[c] = IDF[c + 32] = IDN[c + 32] = true;
            IDF['_'] = IDN['_'] = true;
            for (int c = '0'; c <= '9'; c++) IDN[c] = true;
        }

        private Exception Error(string message) { return new Exception(String.Format("{0} at {1} (found: '{2}')", message, at, ((chr < EOF) ? ("\\" + (int)chr) : "EOF"))); }
        private char FromStream() { return (chr = stc[str.Read(stc, 1, 1)]); }
        private char FromString() { return (chr = txt[++at]); }
        private char Next(char ch) { if (chr != ch) throw Error("Unexpected character"); return Read(); }
        private void Reset(Func<char> read) { at = -1; chr = ANY; Read = read; }
        private void Space() { if (chr <= ' ') while (Read() <= ' ') ; }

        private object Error(int outer) { throw Error("Bad value"); }
        private object Null(int outer) { Next('n'); Next('u'); Next('l'); Next('l'); return null; }
        private object False(int outer) { Next('f'); Next('a'); Next('l'); Next('s'); Next('e'); return false; }
        private object True(int outer) { Next('t'); Next('r'); Next('u'); Next('e'); return true; }

        private void Append(char ch)
        {
            if (len >= LBS)
            {
                if (lsb.Length == 0)
                    lsb.Append(new string(lbf, 0, len));
                lsb.Append(ch);
            }
            else
                lbf[len++] = ch;
        }

        private object Num(int outer)
        {
            string s;
            lsb.Length = 0; len = 0;
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
                while ((Read() >= '0') && (chr <= '9'))
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
            s = ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, len));
            return ((outer > STRING) ? Convert.ChangeType(s, types[outer].Type) : s);
        }

        private object Str(int outer)
        {
            bool eos = false, esc = false;
            string s;
            if (chr == '"')
            {
                lsb.Length = 0; len = 0;
                while (!eos)
                {
                    Read();
                    switch (chr)
                    {
                        case '\\':
                            esc = true;
                            Read();
                            break;
                        case '"':
                            Read();
                            s = ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, len));
                            return ((outer > STRING) ? Convert.ChangeType(s, types[outer].Type) : s);
                        default:
                            break;
                    }
                    eos |= (chr == EOF);
                    if (!eos) Append((esc && (chr < 128)) ? ESC[chr] : chr);
                    esc = false;
                }
            }
            throw Error((outer > OBJECT) ? "Bad literal" : "Bad key");
        }

        private object Obj(int outer)
        {
            IDictionary hash = null;
            object obj = null;
            if (outer > OBJECT)
                obj = types[outer].Ctor();
            else
                hash = new Dictionary<string, object>();
            if (chr == '{')
            {
                Read();
                Space();
                if (chr == '}')
                {
                    Read();
                    return (obj ?? hash);
                }
                while (chr < EOF)
                {
                    var key = (string)Str(OBJECT);
                    Space();
                    Next(':');
                    Space();
                    if ((outer > OBJECT) && (key.Length > 0))
                    {
                        //FIXME: quick hack to be able to pass the burning monk's deserialization test, at:
                        //https://github.com/theburningmonk/SimpleSpeedTester
                        if (key[0] != '$')
                        {
                            PropInfo pi;
                            if (types[outer].Props.TryGetValue(key, out pi))
                                pi.Set(obj, Val(Entry(pi.Type)));
                            else
                                Val(OBJECT);
                        }
                        else
                            Val(OBJECT);
                    }
                    else
                        hash.Add(key, Val(OBJECT));
                    Space();
                    if (chr == '}')
                    {
                        Read();
                        return (obj ?? hash);
                    }
                    Next(',');
                    Space();
                }
            }
            throw Error("Bad object");
        }

        private object Arr(int outer)
        {
            IList list;
            outer = ((outer >= ARRAY) ? outer : ARRAY);
            if (chr == '[')
            {
                Read();
                Space();
                list = (IList)types[outer].Ctor();
                if (chr == ']')
                {
                    Read();
                    if (types[outer].Type.IsArray)
                    {
                        var array = Array.CreateInstance(types[outer].ElementType, list.Count);
                        list.CopyTo(array, 0);
                        return array;
                    }
                    else
                        return list;
                }
                while (chr < EOF)
                {
                    list.Add(Val(types[outer].Inner));
                    Space();
                    if (chr == ']')
                    {
                        Read();
                        if (types[outer].Type.IsArray)
                        {
                            var array = Array.CreateInstance(types[outer].ElementType, list.Count);
                            list.CopyTo(array, 0);
                            return array;
                        }
                        else
                            return list;
                    }
                    Next(',');
                    Space();
                }
            }
            throw Error("Bad array");
        }

        private Type GetElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();
            else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return (type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object));
            else
                return null;
        }

        private int Entry(Type type)
        {
            return Entry(type, null);
        }

        private int Entry(Type type, Type elem)
        {
            int outer;
            if (!rtti.TryGetValue(type, out outer))
            {
                bool b = (elem != null);
                outer = rtti.Count;
                elem = (elem ?? GetElementType(type));
                types[outer] = new TypeInfo(type, Ctor((elem ?? type), (elem != null)));
                rtti.Add(type, outer);
                if (elem != null)
                {
                    types[outer].ElementType = elem;
                    if (!b) types[outer].Inner = Entry(elem);
                }
            }
            return outer;
        }

        private object Val(int outer)
        {
            Space();
            return parse[chr & 0x7f](outer);
        }

        private T DoParse<T>(System.IO.Stream input)
        {
            Reset(FromStream);
            using (str = new System.IO.StreamReader(input))
            {
                return (T)Val(Entry(typeof(T)));
            }
        }

        private T DoParse<T>(string input)
        {
            var len = input.Length;
            txt = new char[len + 1];
            txt[len] = EOF;
            input.CopyTo(0, txt, 0, len);
            Reset(FromString);
            return (T)Val(Entry(typeof(T)));
        }

        public JsonParser()
        {
            parse['n'] = Null; parse['f'] = False; parse['t'] = True;
            parse['0'] = parse['1'] = parse['2'] = parse['3'] = parse['4'] =
            parse['5'] = parse['6'] = parse['7'] = parse['8'] = parse['9'] =
            parse['-'] = Num; parse['"'] = Str; parse['{'] = Obj; parse['['] = Arr;
            for (var input = 0; input < 128; input++) parse[input] = (parse[input] ?? Error);
            Entry(typeof(object));
            Entry(typeof(object[]), typeof(object));
            Entry(typeof(string), typeof(char));
            Entry(typeof(double));
        }

        public T Parse<T>(System.IO.Stream input)
        {
            if (input == null) throw new ArgumentNullException("input", "cannot be null");
            return DoParse<T>(input);
        }

        public T Parse<T>(string input)
        {
            if (input == null) throw new ArgumentNullException("input", "cannot be null");
            return DoParse<T>(input);
        }
    }
}

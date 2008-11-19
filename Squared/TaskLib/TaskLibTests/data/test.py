import util
import types
from xml.sax.handler import *

StartDocument = 0
EndDocument = 1
StartElement = 2
EndElement = 3
IgnorableWhitespace = 4
Characters = 5

def storeInDict(target, key, value):
    target[key] = value
    
def storeInExistingAttribute(target, key, value, converter=None):
    if hasattr(target, key):
        if converter:
            setattr(target, key, converter(value))
        else:
            setattr(target, key, value)

def storeInAttribute(target, key, value):
    setattr(target, key, value)

def readAttributes(target, attributes):
    for k in attributes.getNames():
        setattr(target, k, attributes[k])

def stripWhitespace(text):
    lines = [x.strip() for x in text.replace('\r', '').split('\n')]
    return ' '.join(lines).strip()

class Return(object):
    def __init__(self, value=None):
        self.value = value

class ParseState(object):
    def __init__(self, output):
        self.token = None
        self.output = output
        self.current = None
        self.result = None
        self.stack = []

    def fillDictionary(self, target, storer = storeInDict):
        depth = 0
        key = None
        buffer = None
        while True:
            yield
            type = self.token[0]
            if type == StartElement:
                depth += 1
                key = self.token[1]
                buffer = ""
            elif type == EndElement:
                if key != None:
                    storer(target, key, buffer)
                    key = None
                    buffer = None
                depth -= 1
                if depth < 0:
                    break
            elif type == IgnorableWhitespace:
                if key != None:
                    buffer += self.token[1]
            elif type == Characters:
                if key != None:
                    buffer += self.token[1]
    
    def readContent(self, handler, result=None):
        buffer = ""
        while True:
            type = self.token[0]
            if type == IgnorableWhitespace:
                buffer += self.token[1]
            elif type == Characters:
                buffer += self.token[1]
            elif type == EndElement:
                break
            yield
        
        if isinstance(handler, types.ListType):
            handler[0] = buffer
        else:
            handler(buffer)
        
        if result:
            yield Return(result)
    
    def readInts(self, buffer):
        def myHandler(text):
            values = stripWhitespace(text).split(" ")
            buffer.extend(map(int, values))
        
        return self.readContent(myHandler)
    
    def readFloats(self, buffer):
        def myHandler(text):
            values = stripWhitespace(text).split(" ")
            buffer.extend(map(float, values))
        
        return self.readContent(myHandler)

class ColladaParseHandler(ContentHandler):
    def __init__(self, state):
        self.state = state
        self.state.stack = []
        self.state.current = self.state.main()
        self._debug = False
    
    def _getDebugState(self):
        return "TOKEN: %r STACK: %s" % (self.state.token, ",".join([util.lifetime.best_repr(x) for x in self.state.stack]))
    
    def _push(self, handler):
        if self._debug:
            print "+%s" % (util.lifetime.best_repr(handler),)
        self.state.stack.append(self.state.current)
        self.state.current = handler
        if len(self.state.stack) > 512:
            raise Exception("Handler stack overflow", self._getDebugState())
    
    def _pop(self):
        if self._debug:
            print "-%s" % (util.lifetime.best_repr(self.state.current),)
        if len(self.state.stack) > 0:
            self.state.current = self.state.stack.pop()
        else:
            self.state.current = None
    
    def _step(self):
        newHandler = None
        try:
            if self.state.result != None:
                self.state.current.send(self.state.result)
                self.state.result = None
            else:
                newHandler = self.state.current.next()
        except StopIteration:
            self._pop()
        except Exception, e:
            if self._debug:
                print e, self._getDebugState()
            raise
        
        if newHandler != None:
            if isinstance(newHandler, Return):
                self.state.result = newHandler.value
                self._pop()
                return True
            else:
                self._push(newHandler)
                return False
        else:
            return True
    
    def token(self, type, *args):
        self.state.output._tokensRead += 1
        t = (type,) + args
        self.state.token = t
        if (type == StartElement) and self._debug:
            print "<" + args[0] + ">"
        elif (type == EndElement) and self._debug:
            print "</" + args[0] + ">"
        
        while True:
            r = self._step()
            if r:
                break

    def startDocument(self):
        self.token(StartDocument)
    
    def endDocument(self):
        self.token(EndDocument)
    
    def startElement(self, name, attributes):
        self.state.output._elementsRead += 1
        self.token(StartElement, name, attributes)
    
    def endElement(self, name):
        self.token(EndElement, name)
    
    def ignorableWhitespace(self, whitespace):
        self.token(IgnorableWhitespace, whitespace)
    
    def characters(self, content):
        self.token(Characters, content)
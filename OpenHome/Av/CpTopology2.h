#ifndef HEADER_TOPOLOGY2
#define HEADER_TOPOLOGY2

#include <OpenHome/OhNetTypes.h>
#include <OpenHome/Private/Fifo.h>
#include <OpenHome/Private/Thread.h>
#include <OpenHome/Net/Core/CpAvOpenhomeOrgProduct1.h>
#include <OpenHome/Net/Core/CpAvOpenhomeOrgVolume1.h>

#include <vector>

#include "CpTopology1.h"

namespace OpenHome {
    namespace Net {
        class CpStack;
    } // namespace Net
namespace Av {

class CpTopology2Group;

class ICpTopology2Handler
{
public:
    virtual void GroupAdded(CpTopology2Group& aGroup) = 0;
    virtual void GroupStandbyChanged(CpTopology2Group& aGroup) = 0;
    virtual void GroupSourceIndexChanged(CpTopology2Group& aGroup) = 0;
    virtual void GroupSourceListChanged(CpTopology2Group& aGroup) = 0;
    virtual void GroupRemoved(CpTopology2Group& aDevice) = 0;
    ~ICpTopology2Handler() {}
};

class ICpTopology2GroupHandler
{
public:
	virtual void AddRef() = 0;
	virtual void RemoveRef() = 0;
    virtual void SetSourceIndex(TUint aIndex) = 0;
    virtual void SetStandby(TBool aValue) = 0;
    ~ICpTopology2GroupHandler() {}
};

class CpTopology2Source
{
    friend class CpTopology2Group;
    
    static const TUint kMaxNameBytes = 64;
    static const TUint kMaxTypeBytes = 20;

private:
    CpTopology2Source(const Brx& aName, const Brx& aType, TBool aVisible);
    const Brx& Name() const;
    const Brx& Type() const;
    TBool Visible() const;
    void Update(const Brx& aName, const Brx& aType, TBool aVisible);
    
private:
    Bws<kMaxNameBytes> iName;
    Bws<kMaxTypeBytes> iType;
    TBool iVisible;
};

class CpTopology2Group
{
    friend class CpTopology2Product;
    friend class CpTopology2MediaRenderer;

    static const TUint kMaxRoomBytes = 64;
    static const TUint kMaxNameBytes = 64;
    
public:
    void AddRef();
    void RemoveRef();
    Net::CpDevice& Device() const;
    TBool Standby() const;
    void SetStandby(TBool aValue);
    const Brx& Room() const;
    const Brx& Name() const;
    TUint SourceCount() const;
    TUint SourceIndex() const;
    void SetSourceIndex(TUint aIndex);
    const Brx& SourceName(TUint aIndex) const;
    const Brx& SourceType(TUint aIndex) const;
    TBool SourceVisible(TUint aIndex) const;
    TBool HasVolumeControl() const;
    void SetUserData(void* aValue);
    void* UserData() const;
    
public: // for test purposes
    CpTopology2Group(Net::CpDevice& aDevice, ICpTopology2GroupHandler& aHandler, TBool aStandby, const Brx& aRoom, const Brx& aName, TUint aSourceIndex, TBool aHasVolumeControl);
    void AddSource(const Brx& aName, const Brx& aType, TBool aVisible);
    void UpdateRoom(const Brx& aValue);
    void UpdateName(const Brx& aValue);
    void UpdateSourceIndex(TUint aValue);
    void UpdateStandby(TBool aValue);
    void UpdateSource(TUint aIndex, const Brx& aName, const Brx& aType, TBool aVisible);
    ~CpTopology2Group();

private:
    Net::CpDevice& iDevice;
    ICpTopology2GroupHandler& iHandler;
    TBool iStandby;
    Bws<kMaxRoomBytes> iRoom;
    Bws<kMaxNameBytes> iName;
    TUint iSourceIndex;
    TBool iHasVolumeControl;
    void* iUserData;
    TUint iRefCount;
    std::vector<CpTopology2Source*> iSourceList;
};

typedef void (ICpTopology2Handler::*ICpTopology2HandlerFunction)(CpTopology2Group&);

class CpTopology2Job
{
public:
    CpTopology2Job(ICpTopology2Handler& aHandler);
    void Set(CpTopology2Group& aGroup, ICpTopology2HandlerFunction aFunction);
    virtual void Execute();
	virtual ~CpTopology2Job() {}
private:
    ICpTopology2Handler* iHandler;
    CpTopology2Group* iGroup;
    ICpTopology2HandlerFunction iFunction;
};

class CpTopology2Device : public INonCopyable, public ICpTopology2GroupHandler
{
protected:
    CpTopology2Device(Net::CpDevice& aDevice);
	virtual ~CpTopology2Device();
    
public:
	virtual void RemoveGroup() = 0;
    TBool IsAttachedTo(Net::CpDevice& aDevice);

	// ICpTopology2GroupHandler
	virtual void AddRef() = 0;
    virtual void RemoveRef() = 0;

private:
    // ICpTopology2GroupHandler
    virtual void SetSourceIndex(TUint aIndex) = 0;
    virtual void SetStandby(TBool aValue) = 0;

protected:
    Net::CpDevice& iDevice;
};

class CpTopology2Product : public CpTopology2Device 
{
public:
    CpTopology2Product(Net::CpDevice& aDevice, ICpTopology2Handler& aHandler);

	virtual void RemoveGroup();

	// ICpTopology2GroupHandler
	virtual void AddRef();
    virtual void RemoveRef();

protected:
	virtual ~CpTopology2Product();

private:
    // ICpTopology2GroupHandler
    virtual void SetSourceIndex(TUint aIndex);
    virtual void SetStandby(TBool aValue);

    void CallbackSetSourceIndex(Net::IAsync& aAsync);        
    void CallbackSetStandby(Net::IAsync& aAsync);        

    void ProcessSourceXml(const Brx& aXml, TBool aInitial);

    void EventProductInitialEvent();
    void EventProductRoomChanged(); 
    void EventProductNameChanged(); 
    void EventProductStandbyChanged();  
    void EventProductSourceIndexChanged();  
    void EventProductSourceXmlChanged();    

private:
    ICpTopology2Handler& iHandler;
    Net::CpProxyAvOpenhomeOrgProduct1* iServiceProduct;
    CpTopology2Group* iGroup;
	TUint iRefCount;
    Net::FunctorAsync iFunctorSetSourceIndex;
    Net::FunctorAsync iFunctorSetStandby;
};

class CpTopology2 : public ICpTopology1Handler, public ICpTopology2Handler
{
    static const TUint kMaxJobCount = 20;
    
public:
    CpTopology2(Net::CpStack& aCpStack, ICpTopology2Handler& aHandler);
    
    void Refresh();
    
    virtual ~CpTopology2();
    
private:
    // ICpTopology1Handler
    virtual void ProductAdded(Net::CpDevice& aDevice);
    virtual void ProductRemoved(Net::CpDevice& aDevice);

    void DeviceRemoved(Net::CpDevice& aDevice);

    // ICpTopology2Handler
    virtual void GroupAdded(CpTopology2Group& aGroup);
    virtual void GroupStandbyChanged(CpTopology2Group& aGroup);
    virtual void GroupSourceIndexChanged(CpTopology2Group& aGroup);
    virtual void GroupSourceListChanged(CpTopology2Group& aGroup);
    virtual void GroupRemoved(CpTopology2Group& aDevice);

    void Run();
    
private:
    CpTopology1* iTopology1;
    Fifo<CpTopology2Job*> iFree;
    Fifo<CpTopology2Job*> iReady;
    ThreadFunctor* iThread;
    std::vector<CpTopology2Device*> iDeviceList;
};

} // namespace Av
} // namespace OpenHome

#endif // HEADER_TOPOLOGY2

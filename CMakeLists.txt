cmake_minimum_required(VERSION 2.8)

project(ohTopology CXX)

option(BUILD_SHARED_LIBS "Build ohTopology as a shared library" ON)
option(BUILD_STATIC_LIBS "Build ohTopology as a static library" ON)

if (NOT BUILD_SHARED_LIBS AND NOT BUILD_STATIC_LIBS)
    message(FATAL_ERROR "One or both of BUILD_SHARED_LIBS or BUILD_STATIC_LIBS"
                        "must be set to ON to build")
endif()

if(NOT DEFINED CMAKE_INSTALL_LIBDIR)
    SET(CMAKE_INSTALL_LIBDIR lib CACHE PATH "Output directory for libraries")
endif()

if(NOT DEFINED CMAKE_INSTALL_INCLUDEDIR)
    SET(CMAKE_INSTALL_INCLUDEDIR include CACHE
        PATH "Output directory for header files")
endif()

find_path(OHNET_INCLUDE_DIRS
	NAMES OpenHome/OhNetTypes.h
	PATH_SUFFIXES ohNet
)
if (OHNET_INCLUDE_DIRS)
    include_directories(${OHNET_INCLUDE_DIRS})
else ()
    message(FATAL_ERROR "ERROR: you need the ohNet header files installed.")
endif ()

find_library(OHNETCORE_LIBRARY NAMES ohNetCore PATH_SUFFIXES ohNet)
if (OHNETCORE_LIBRARY)
    set(LIBS ${LIBS} ${OHNETCORE_LIBRARY})
else ()
    message(FATAL_ERROR "ERROR: you need the ohNet library installed.")
endif ()

find_library(OHNETGENERATEDPROXIES_LIBRARY NAMES ohNetGeneratedProxies PATH_SUFFIXES ohNetGenerated)
if (OHNETGENERATEDPROXIES_LIBRARY)
    set(LIBS ${LIBS} ${OHNETGENERATEDPROXIES_LIBRARY})
else ()
    message(FATAL_ERROR "ERROR: you need the ohNetGenerated library installed.")
endif ()

include_directories(OpenHome/Av)
set(OHTOPOLOGY_SOURCES OpenHome/Av/CpTopology.cpp
                       OpenHome/Av/CpTopology1.cpp
                       OpenHome/Av/CpTopology2.cpp
                       OpenHome/Av/CpTopology3.cpp
                       OpenHome/Av/CpTopology4.cpp)

if (BUILD_SHARED_LIBS)
    add_library(ohtopology SHARED "${OHTOPOLOGY_SOURCES}")
    target_link_libraries(ohtopology ${LIBS})
    install(TARGETS ohtopology LIBRARY DESTINATION "${CMAKE_INSTALL_LIBDIR}")
endif (BUILD_SHARED_LIBS)

if (BUILD_STATIC_LIBS)
    add_library(ohtopology-static STATIC "${OHTOPOLOGY_SOURCES}")
    target_link_libraries(ohtopology-static ${LIBS})
    set_target_properties(ohtopology-static PROPERTIES OUTPUT_NAME ohtopology)
    install(TARGETS ohtopology-static ARCHIVE DESTINATION "${CMAKE_INSTALL_LIBDIR}")
endif (BUILD_STATIC_LIBS)

install(FILES OpenHome/Av/CpTopology.h
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}/OpenHome/Av")

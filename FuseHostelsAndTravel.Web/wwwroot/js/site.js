const $dropdown = $(".dropdown");
const $dropdownToggle = $(".dropdown-toggle");
const $dropdownMenu = $(".dropdown-menu");
const $showClass = "show";
let $windowWidth = null;

$(window).on('beforeunload', function () {
    showLoader();
    window.scrollTo(0, 0);
});

$(window).on("scroll", function () {
    adjustNavbar();

    $(".has-parallax-scroll").each(function () {
        var position = $(this).offset().top;
        var scrollPosition = $(window).scrollTop() + $(window).height();
        if (scrollPosition > position) {
            $(this).addClass("fade-in");
        }
    });
});

$(window).on("resize", function () {
    bindFuseJs();
    adjustNavbar();
    setNavigationDropdownHover();
    //setMobileCarouselImages();
});

$(window).on("load", function () {
    bindFuseJs();
    adjustNavbar();
    setNavigationDropdownHover();
    configureDatepickers();
    configureSelects();
    setParallaxMarquee();
    getCookie();
    //setMobileCarouselImages();

    $('.navbar-toggler').on('click', function () {
        $('.animated-icon3').toggleClass('open');

        if ($('.navbar').hasClass('bg-dark')) {
            window.setTimeout(function () {
                $('.navbar').removeClass('bg-dark');

                if ($('.navbar').hasClass('shadow')) {
                    $('.navbar-toggler i').addClass('text-dark').removeClass('text-white');
                    $('.animated-icon3 span').css('background', '#090511');
                }
            }, 200);

        }
        else {
            $('.navbar').addClass('bg-dark');
            $('.navbar-toggler i').addClass('text-white').removeClass('text-dark');
            $('.animated-icon3 span').css('background', '#FFFFFF');
            $('.navbar .nav-link').addClass('text-white');
        }
    });

    window.setTimeout(function () {
        history.replaceState("", document.title, window.location.pathname);
    }, 0);

    window.setTimeout(function () {
        window.scrollTo(0, 0);
    }, 100);

    window.setTimeout(function () {
        hideLoader();

        $(".has-parallax-scroll").each(function () {
            if ($(this).is(':visible')) {
                var position = $(this).offset().top;
                var scrollPosition = $(window).scrollTop() + $(window).height();
                if (scrollPosition > position) {
                    $(this).addClass("fade-in");
                }
            }
        });
    }, 800);

    $('.accordion-button').on('click', function () {
        $('.accordion-collapse').removeClass('show');
        $('.accordion-button').addClass('collapsed');

        var icon = $(this).find('i');
        if (icon.hasClass('fa-caret-down'))
            icon.removeClass('fa-caret-down').addClass('fa-caret-up');
        else
            icon.removeClass('fa-caret-up').addClass('fa-caret-down');

        $('.accordion-button i').not(icon).removeClass('fa-caret-up').addClass('fa-caret-down');
    });

    $('#bookNowForm, #bookNowModalForm').on('submit', function () {
        validateSelectElements($(this));
    });

    $('#download-data').on('submit', function () {
        setTimeout(function () {
            hideLoader();
        }, 1000);
    });

    $('#loginModalForm').submit(function (e) {
        if (!$(this).attr('validated')) {
            if ($(this).valid()) {
                $('#loginModalButton').prop('disabled', true);
                postAjax("ValidateUser", { "Username": $('#LoginModal_Email').val(), "Password": $('#LoginModal_Password').val() }, function (result) {
                    if (result.success != true) {
                        e.preventDefault();
                        e.stopImmediatePropagation();
                        
                        $('#feedbackAlertMessage').html(result.message);
                        mdb.Alert.getInstance(document.getElementById('feedbackAlert')).show();

                        $('#loginModalButton').prop('disabled', false);

                        return false;
                    }
                    else {
                        $('#loginModalForm').attr('validated', true);
                        $('#loginModalForm').submit();
                    }

                    $('#loginModalButton').prop('disabled', false);
                }, true);

                return false;
            }
        }

        return true;
    });

    $('#registerModalForm').submit(function (e) {
        if (!$(this).attr('validated')) {
            if ($(this).valid()) {
                $('#registerModalButton').prop('disabled', true);
                postAjax("CheckIfUserExists", { "Username": $('#RegisterModal_Email').val(), "Password": $('#RegisterModal_Password').val() }, function (result) {
                    if (result.success != true) {
                        e.preventDefault();
                        e.stopImmediatePropagation();

                        $('#feedbackAlertMessage').html(result.message);
                        mdb.Alert.getInstance(document.getElementById('feedbackAlert')).show();

                        $('#registerModalButton').prop('disabled', false);

                        return false;
                    }
                    else {
                        $('#registerModalForm').attr('validated', true);
                        $('#registerModalForm').submit();
                    }

                    $('#registerModalButton').prop('disabled', false);
                }, true);

                return false;
            }
        }

        return true;
    });

    $('#feedbackAlert button').on('click', function () {
        mdb.Alert.getInstance(document.getElementById('feedbackAlert')).hide();
    });

    let backToTopButton = document.getElementById("btn-back-to-top");

    window.onscroll = function () {
        scrollFunction();
    };

    function scrollFunction() {
        if (
            document.body.scrollTop > 20 ||
            document.documentElement.scrollTop > 20
        ) {
            backToTopButton.style.display = "block";
        } else {
            backToTopButton.style.display = "none";
        }
    }

    backToTopButton.addEventListener("click", backToTop);

    function backToTop() {
        document.body.scrollTop = 0;
        document.documentElement.scrollTop = 0;
    }
});

var setMobileNav = function () {
    if (isMobile())
        $('.navbar, .sticky-top').addClass('shadow');
    else
        $('.navbar, .sticky-top').removeClass('shadow');
}

var setMobileCarouselImages = function () {

    if (isMobile()) {
        $('#fullPageCarousel').find('.carousel-item').each(function () {
            let backgroundImage = $(this).css('background-image');
            backgroundImage = backgroundImage.replace('w-2000', 'w-1000');

            $(this).css('background-image', backgroundImage);
        });
    }
    else {
        $('#fullPageCarousel').find('.carousel-item').each(function () {
            let backgroundImage = $(this).css('background-image');
            backgroundImage = backgroundImage.replace('w-1000', 'w-2000');

            $(this).css('background-image', backgroundImage);
        });
    }
}

var getWindowWidth = function () {
    return $(window).width();
}

var isMobile = function () {
    return getWindowWidth() <= 992;
}

var setNavigationDropdownHover = function () {
    $windowWidth = getWindowWidth();

    if ($windowWidth > 992) {
        $dropdown.hover(
            function () {
                const $this = $(this);
                $this.addClass($showClass);
                $this.find($dropdownToggle).attr("aria-expanded", "true");
                $this.find($dropdownMenu).addClass($showClass);
            },
            function () {
                const $this = $(this);
                $this.removeClass($showClass);
                $this.find($dropdownToggle).attr("aria-expanded", "false");
                $this.find($dropdownMenu).removeClass($showClass);
            }
        );
    } else {
        $dropdown.off("mouseenter mouseleave");
    }
}

var showLoader = function () {
    $('.pre-loader').show();
    $('html').addClass('no-scrollbar');
}

var hideLoader = function () {
    $('html').removeClass('no-scrollbar');
    $('.pre-loader').hide()
}

var bindFuseJs = function () {
    adjustCardMargain();
    adjustCircularImages();
    setMultiItemCarousel();

    if ($('.inherit-sticky-parent').length > 0) {
        $('.inherit-sticky-parent').each(function () {
            let stickyHeight = $('.sticky-top[data-mdb-sticky-boundary]').outerHeight();
            $(this).css('margin-top', '-' + stickyHeight + 'px');
            $(this).find('.offset-background').css('background-image', '-webkit-linear-gradient(-50deg, #FFFFFF 70%, #D1AC00 50%)');
        });
    }
}

var adjustNavbar = function () {
    if (isMobile()) {
        $('.navbar-collapse').addClass('vh-100')
    }
    else
        $('.navbar-collapse').removeClass('vh-100')

    if (!$('.navbar').hasClass('always-shadow')) {
        if ($(this).scrollTop() > 86) {
            $('.fixed-top').addClass('shadow');

            if (!$('.navbar').hasClass('bg-dark')) {
                $('.navbar-toggler i').addClass('text-dark').removeClass('text-white');
                $('.animated-icon3').find('span').css('background', '#090511');
            }
            else
                $('.navbar .nav-link').addClass('text-white');
        } else {
            if (!$('.navbar-collapse').hasClass('show')) {
                $('.navbar-toggler i').removeClass('text-dark');
                $('.animated-icon3 span').css('background', '#FFFFFF');
                $('.fixed-top').removeClass('shadow');
            }

            if (!isMobile()) {
                $('.navbar-collapse').removeClass('show');
                $('.navbar').removeClass('bg-dark').removeClass('bg-white').removeClass('shadow');
                $('.navbar .nav-link').removeClass('text-white');
            }

        }
    }
    else {
        if (!isMobile()) {
            if ($('.navbar').hasClass('bg-dark')) {
                $('.navbar-collapse').removeClass('show');
                $('.navbar').removeClass('bg-dark').addClass('bg-white');
                $('.navbar .nav-link').removeClass('text-white');
            }
            else {
                $('.navbar .nav-link').removeClass('text-white');
            }
        }
        else {
            if (!$('.navbar').hasClass('bg-dark')) {
                $('.navbar').removeClass('bg-dark')
                $('.navbar-toggler i').addClass('text-dark').removeClass('text-white');
                $('.animated-icon3 span').css('background', '#090511');
            }
            //    
            //else
            //    $('.navbar .nav-link').addClass('text-white');
        }
    }

    window.setTimeout(function () {
        if ($('.navbar').hasClass('always-shadow')) {
            let navbarHeight = $('.navbar').outerHeight().toFixed(2) + "px !important;";
            $('main').attr('style', 'padding-top:' + navbarHeight);
        }
    }, 200);


    if ($('.is-pinned').is(':visible'))
        $('.navbar').addClass('no-shadow');

    if ($('.sticky-top').length > 0) {
        var eTop = $('.sticky-top').offset().top;
        var eTopWindow = eTop - $(window).scrollTop();
        if (eTopWindow == 81 || isMobile() && eTopWindow == 76) {
            $('.navbar').addClass('no-shadow');
            $('.sticky-top').addClass('is-pinned').addClass('bg-white');
        }
        else {
            $('.navbar').removeClass('no-shadow');
            $('.sticky-top').removeClass('is-pinned').removeClass('bg-white');
        }
    }
}

Date.prototype.addDays = function (days) {
    var date = new Date(this.valueOf());
    date.setDate(date.getDate() + days);
    return date;
}

var configureSelects = function () {
    $('.select').each(function () {
        this.addEventListener('valueChange.mdb.select', (e) => {
            let id = e.target.parentElement.id;
            let inputField = $('#' + id).find('.form-control.select-input');

            if (!$(inputField).hasClass('active'))
                $(inputField).addClass('active');
        });

        let value = $(this).val();
        if (value != undefined && value.length > 0) {
            let inputField = $(this).parent().find('.form-control.select-input');

            if (!$(inputField).hasClass('active'))
                $(inputField).addClass('active');
        }
    });
}

var configureDatepickers = function () {
    $('.datepicker-disable-future').each(function () {
        let datepicker = new mdb.Datepicker(this, {
            disableFuture: true,
            toggleButton: false
        });
    });

    $('.datepicker-disable-past').each(function () {
        let datepicker = new mdb.Datepicker(this, {
            disablePast: true,
            toggleButton: false
        });

        this.addEventListener('dateChange.mdb.datepicker', (e) => {
            let id = e.srcElement.children[0].id;
            let date = e.date;

            let correspondingId = ""

            switch (id) {
                case "CarouselComponent_CheckInDate":
                    correspondingId = "CarouselComponent_CheckOutDate"
                    break;
                case "BookNowBanner_CheckInDate":
                    correspondingId = "BookNowBanner_CheckOutDate"
                    break;
                case "CheckInDate":
                    correspondingId = "CheckOutDate"
                    break;
                case "CarouselComponent_CheckOutDate":
                    correspondingId = "CarouselComponent_CheckInDate"
                    break;
                case "BookNowBanner_CheckOutDate":
                    correspondingId = "BookNowBanner_CheckInDate"
                    break;
                case "CheckOutDate":
                    correspondingId = "CheckInDate"
                    break;
            }

            if (id == "CarouselComponent_CheckInDate" || id == "BookNowBanner_CheckInDate" || id == "CheckInDate") {
                let minDate = date.addDays(1);
                let checkOutDatepicker = document.getElementById(correspondingId).parentElement;
                let instance = mdb.Datepicker.getInstance(checkOutDatepicker);

                if (instance) {
                    instance.dispose();
                    instance = new mdb.Datepicker(checkOutDatepicker, {
                        disablePast: true,
                        toggleButton: false,
                        min: minDate,
                        startDate: minDate
                    });
                }
            }
            else if (id == "CarouselComponent_CheckOutDate" || id == "BookNowBanner_CheckOutDate" || id == "CheckOutDate") {
                let maxDate = date;
                let checkInDatePicker = document.getElementById(correspondingId).parentElement;
                let instance = mdb.Datepicker.getInstance(checkInDatePicker);

                if (instance) {
                    instance.dispose();
                    instance = new mdb.Datepicker(checkInDatePicker, {
                        disablePast: true,
                        toggleButton: false,
                        max: maxDate
                    });
                }
            }
        })
    });
}

var adjustCardMargain = function () {
    if ($('.alternating-card-height').length > 0) {
        $('.alternating-card-height').each(function () {
            let $marginBottom = 0;
            let $cards = $(this).find('.card');
            if ($cards.length > 0) {

                $cards.each(function (i, v) {
                    if (i % 2 != 0) {
                        if (isMobile()) {
                            $(v).parent('div').css('top', 'unset');
                            $(v).parent('div').css('margin-bottom', 'unset');
                        }
                        else {
                            if ($marginBottom == 0)
                                $marginBottom = parseFloat($(v).parent('div').css('marginBottom').replace('px', ''));

                            let $marginTop = 315 + $marginBottom;
                            $(v).parent('div').css('top', '-' + $marginTop + 'px');
                            $(v).parent('div').css('margin-bottom', '-' + $marginTop + 'px');
                        }
                    }
                });
            }
        });
    }

    if ($('.lightbox-card').length > 0) {
        $('.lightbox-card').each(function (i, v) {

            if (i % 2 != 0) {
                if (isMobile()) {
                    $(v).css('top', 'unset');
                    $(v).css('margin-bottom', 'unset');
                }
                else {
                    let $marginBottom = $(v).css('marginBottom').replace('px', '');

                    let $marginTop = 315 + $marginBottom;
                    $(v).css('top', $marginBottom + 'px');
                    $(v).css('margin-bottom', '-' + $marginBottom + 'px');
                }
            }
        });
    }
}

var adjustCircularImages = function () {
    $('.full-circle').each(function () {
        let $image = $(this).find('.object-fit-cover');
        let $imageHeight = $image.height();
        let $parentWidth = $(this).parent().width();

        let $previousDivHeight = $(this).parent().prev('div').outerHeight() + 150;

        if ($previousDivHeight > 430)
            $previousDivHeight = 430;

        $previousDivHeight = $parentWidth;

        $(this).width($previousDivHeight);
        $(this).height($previousDivHeight);
        $image.show();
    });

    $('.lightbox-card').each(function () {
        let $image = $(this).find('img');
        let $imageWidth = $image.width();
        $image.width($imageWidth);
        $image.height($imageWidth);
        $image.show();
    });
}

var setMultiItemCarousel = function () {
    if ($('.owl-carousel').length > 0) {
        $('.owl-carousel').each(function () {
            $(this).owlCarousel({
                loop: true,
                margin: 20,
                responsiveClass: true,
                responsive: {
                    0: {
                        items: 1,
                        nav: true
                    },
                    768: {
                        items: 2,
                        nav: true,
                    },
                    1415: {
                        items: 3,
                        nav: true,
                        loop: false
                    }
                },
                onInitialized: function (event) {
                    let element = jQuery(event.target);
                    let idx = event.item.index;
                    element.find('.owl-item').addClass('col-xs-12 col-md-12 col-lg-4');
                    element.find('.owl-stage').addClass('row gx-2');
                },
            });
        });
    }
}

var validateSelectElements = function (form, valid) {
    let $formSelect = form.find('.select');

    if ($formSelect.length > 0)
        $formSelect.each(function (index, select) {
            let selectValid = validateSelectElement(select);

            if (!selectValid && valid)
                valid = false;
        });

    return valid;
}

var validateSelectElement = function (select) {
    let selectValue = $(select).val();
    let validationMessage = $(select).data('val-required');
    let name = $(select).attr('name');

    var validationSpan = $('span[data-valmsg-for="' + name + '"]');

    if (selectValue == null || selectValue.length == 0) {
        validationSpan.removeClass('field-validation-valid').addClass('field-validation-error').html('<span id="' + name + '-error" class="">' + validationMessage + '</span>');

        return false;
    }
    else validationSpan.removeClass('field-validation-error').addClass('field-validation-valid').html('');


    return true;
}

var postAjax = function (url, formData, callback, skipLoader = false) {
    $.ajax({
        type: "POST",
        url: "/?handler=" + url,
        data: JSON.stringify(formData),
        dataType: 'json',
        contentType: 'application/json',
        headers: {
            RequestVerificationToken:
                $('input:hidden[name="__RequestVerificationToken"]').val()
        },
        success: function (data) {
            callback(data);
        },
        error: function (xhr, ajaxOptions, thrownError) {
            console.log(xhr, ajaxOptions, thrownError);
            //window.location.href = "/error";
        }
    });
}

//Url Manipulation
function getBaseUrl() {
    var pathArray = location.href.split('/');
    var protocol = pathArray[0];
    var host = pathArray[2];
    var url = protocol + '//' + host + '/';

    return url;
}

var setParallaxMarquee = function () {
    if ($('.marquee').length > 0) {
        var banners = [
            { element: $('.marquee:nth-of-type(1)'), direction: 'left', factor: 7 },
            { element: $('.marquee:nth-of-type(2)'), direction: 'right', factor: 8 },
            { element: $('.marquee:nth-of-type(3)'), direction: 'left', factor: 9 }
        ];

        banners.forEach(function (banner) {
            var bannerOffset = banner.element.offset().left;
            var bannerWidth = banner.element.width();
            var lastScrollPosition = -1;

            if (banner.direction === 'left') {
                banner.element.css({
                    'transform': 'translateX(' + (-bannerWidth) + 'px)'
                });
            } else {
                banner.element.css({
                    'transform': 'translateX(' + ($(window).width() - bannerOffset) + 'px)'
                });
            }

            $(window).scroll(function () {
                var scroll = $(window).scrollTop();
                var delta = lastScrollPosition === -1 ? 0 : scroll - lastScrollPosition;
                lastScrollPosition = scroll;
                requestAnimationFrame(function () {
                    if (banner.direction === 'left') {
                        var bannerPosition = -scroll / banner.factor - bannerOffset;
                        if (bannerPosition < -bannerWidth) {
                            bannerPosition += bannerWidth;
                        }
                        banner.element.css({
                            'transform': 'translateX(' + bannerPosition + 'px)'
                        });
                    } else {
                        var bannerPosition = scroll / banner.factor + ($(window).width() - bannerOffset);
                        if (bannerPosition > $(window).width()) {
                            bannerPosition -= bannerWidth;
                        }
                        banner.element.css({
                            'transform': 'translateX(' + bannerPosition + 'px)'
                        });
                    }
                });
            });
        });
    }
}

var getCookie = function () {
    var cookie = Cookie.get('fuse-hostels-and-travel');

    if (cookie == undefined) {
        window.setTimeout(function () {
            let cookiesModalEl = document.getElementById('cookiesModal');
            let modal = new mdb.Modal(cookiesModalEl);
            modal.show();
        }, 2000);
    }
}
var confirmCookie = function () {
    Cookie.set('fuse-hostels-and-travel', new Date(), { expires: 365 });
    $('#cookiesModal').modal('hide');
}